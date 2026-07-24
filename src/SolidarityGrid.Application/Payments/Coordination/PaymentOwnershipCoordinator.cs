using System.Security.Cryptography;
using System.Text;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed class PaymentOwnershipCoordinator(
    IPaymentCoordinationTransport transport,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IMeshNodeIdentity localIdentity,
    IMeshPeerDirectory peerDirectory,
    PaymentProcessingOptions options,
    IPaymentCoordinationObserver observer)
{
    private const int MaximumRounds = 2;

    public async Task<PaymentOwnershipResult> TryAcquireAsync(
        Payment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var isTakeover =
            payment.Status is PaymentStatus.Claimed or PaymentStatus.Processing;
        var previousOwnerNodeId = payment.OwnerNodeId?.Value;
        var previousTerm = payment.Term;
        var previousLeaseExpiresAtUtc = payment.LeaseExpiresAtUtc;
        var eligibilityTime = timeProvider.GetUtcNow();
        if (payment.Status != PaymentStatus.Replicated &&
            (!isTakeover || !payment.CanBeTakenOver(eligibilityTime)))
        {
            return PaymentOwnershipResult.NotAcquired(
                0,
                isTakeover,
                previousOwnerNodeId,
                previousTerm);
        }

        await WaitForContentionTurnAsync(
            payment.Id,
            isTakeover,
            cancellationToken);
        var proposedTerm = payment.Term + 1;
        for (var round = 1; round <= MaximumRounds; round++)
        {
            var occurredAtUtc = timeProvider.GetUtcNow();
            var leaseExpiresAtUtc = occurredAtUtc.AddMilliseconds(
                options.LeaseDurationMilliseconds);
            var request = new PaymentClaimRequest(
                payment.Id,
                localIdentity.NodeId,
                proposedTerm,
                leaseExpiresAtUtc,
                occurredAtUtc);

            if (isTakeover)
            {
                observer.TakeoverStarted(
                    payment.Id,
                    correlationId,
                    previousOwnerNodeId!,
                    previousTerm,
                    localIdentity.NodeId.Value,
                    proposedTerm,
                    previousLeaseExpiresAtUtc!.Value);
            }

            observer.ClaimStarted(payment.Id, correlationId, proposedTerm);
            var peerResults = await transport.TryClaimAsync(
                request,
                correlationId,
                cancellationToken);
            foreach (var peerResult in peerResults)
            {
                observer.ClaimPeerResult(
                    payment.Id,
                    correlationId,
                    peerResult.PeerNodeId.Value,
                    peerResult.Granted,
                    proposedTerm,
                    peerResult.ErrorCode);
            }

            if (peerResults.Any(result => result.Granted))
            {
                try
                {
                    var changed = payment.Claim(
                        localIdentity.NodeId,
                        proposedTerm,
                        leaseExpiresAtUtc,
                        occurredAtUtc);
                    if (changed)
                    {
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    observer.OwnershipAcquired(
                        payment.Id,
                        correlationId,
                        localIdentity.NodeId.Value,
                        proposedTerm);
                    if (isTakeover)
                    {
                        observer.TakeoverAcquired(
                            payment.Id,
                            correlationId,
                            previousOwnerNodeId!,
                            previousTerm,
                            localIdentity.NodeId.Value,
                            proposedTerm);
                    }

                    return PaymentOwnershipResult.Acquired(
                        proposedTerm,
                        leaseExpiresAtUtc,
                        round,
                        isTakeover,
                        previousOwnerNodeId,
                        previousTerm,
                        localIdentity.NodeId.Value);
                }
                catch (PaymentDomainException)
                {
                    RejectTakeover(
                        payment,
                        correlationId,
                        isTakeover,
                        previousOwnerNodeId,
                        previousTerm,
                        proposedTerm,
                        PaymentCoordinationErrorCodes.Failed);
                    return PaymentOwnershipResult.NotAcquired(
                        round,
                        isTakeover,
                        previousOwnerNodeId,
                        previousTerm);
                }
                catch (PaymentConcurrencyException)
                {
                    RejectTakeover(
                        payment,
                        correlationId,
                        isTakeover,
                        previousOwnerNodeId,
                        previousTerm,
                        proposedTerm,
                        PaymentCoordinationErrorCodes.ClaimTermStale);
                    return PaymentOwnershipResult.NotAcquired(
                        round,
                        isTakeover,
                        previousOwnerNodeId,
                        previousTerm);
                }
            }

            if (peerResults.Any(result =>
                    result.ErrorCode ==
                    PaymentCoordinationErrorCodes.ClaimLeaseActive))
            {
                RejectTakeover(
                    payment,
                    correlationId,
                    isTakeover,
                    previousOwnerNodeId,
                    previousTerm,
                    proposedTerm,
                    PaymentCoordinationErrorCodes.ClaimLeaseActive);
                return PaymentOwnershipResult.NotAcquired(
                    round,
                    isTakeover,
                    previousOwnerNodeId,
                    previousTerm);
            }

            var staleResults = peerResults
                .Where(result =>
                    result.ErrorCode ==
                    PaymentCoordinationErrorCodes.ClaimTermStale)
                .ToArray();
            if (round < MaximumRounds && staleResults.Length > 0)
            {
                proposedTerm = Math.Max(
                    proposedTerm,
                    staleResults.Max(result => result.CurrentTerm)) + 1;
                continue;
            }

            var unavailable = peerResults.Count == 0 ||
                              peerResults.All(result =>
                                  result.ErrorCode is
                                      PaymentCoordinationErrorCodes.PeerUnavailable or
                                      PaymentCoordinationErrorCodes.DeadlineExceeded or
                                      PaymentCoordinationErrorCodes.Failed);
            var errorCode = unavailable
                ? PaymentCoordinationErrorCodes.QuorumUnavailable
                : staleResults.Length > 0
                    ? PaymentCoordinationErrorCodes.ClaimTermStale
                    : PaymentCoordinationErrorCodes.Failed;
            RejectTakeover(
                payment,
                correlationId,
                isTakeover,
                previousOwnerNodeId,
                previousTerm,
                proposedTerm,
                errorCode);
            return unavailable
                ? PaymentOwnershipResult.QuorumUnavailable(
                    round,
                    isTakeover,
                    previousOwnerNodeId,
                    previousTerm)
                : PaymentOwnershipResult.NotAcquired(
                    round,
                    isTakeover,
                    previousOwnerNodeId,
                    previousTerm);
        }

        return PaymentOwnershipResult.NotAcquired(
            MaximumRounds,
            isTakeover,
            previousOwnerNodeId,
            previousTerm);
    }

    private void RejectTakeover(
        Payment payment,
        string correlationId,
        bool isTakeover,
        string? previousOwnerNodeId,
        long previousTerm,
        long proposedTerm,
        string errorCode)
    {
        if (!isTakeover)
        {
            return;
        }

        observer.TakeoverRejected(
            payment.Id,
            correlationId,
            previousOwnerNodeId!,
            previousTerm,
            localIdentity.NodeId.Value,
            proposedTerm,
            errorCode);
    }

    private async Task WaitForContentionTurnAsync(
        Guid paymentId,
        bool isTakeover,
        CancellationToken cancellationToken)
    {
        var orderedCandidates = peerDirectory
            .GetPeers()
            .Select(peer => peer.NodeId)
            .Append(localIdentity.NodeId)
            .Distinct()
            .OrderBy(nodeId => Priority(paymentId, nodeId), StringComparer.Ordinal)
            .ThenBy(nodeId => nodeId.Value, StringComparer.Ordinal)
            .ToArray();
        var rank = Array.IndexOf(orderedCandidates, localIdentity.NodeId);
        if (rank <= 0)
        {
            return;
        }

        // Recovery cycles are independently phased. A lease-sized slot keeps
        // adjacent candidates apart long enough to avoid crossed remote claims.
        var slotMilliseconds = isTakeover
            ? options.LeaseDurationMilliseconds
            : Math.Max(
                1,
                options.ScanIntervalMilliseconds /
                (orderedCandidates.Length + 1));
        await Task.Delay(
            TimeSpan.FromMilliseconds(rank * slotMilliseconds),
            timeProvider,
            cancellationToken);
    }

    private static string Priority(Guid paymentId, NodeId nodeId)
    {
        var value = Encoding.UTF8.GetBytes(
            $"{paymentId:N}:{nodeId.Value}");
        return Convert.ToHexString(SHA256.HashData(value));
    }
}
