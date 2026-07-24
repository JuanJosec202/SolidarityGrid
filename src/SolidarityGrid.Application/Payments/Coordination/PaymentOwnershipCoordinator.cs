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

        if (payment.Status != PaymentStatus.Replicated)
        {
            return PaymentOwnershipResult.NotAcquired(0);
        }

        await WaitForContentionTurnAsync(payment.Id, cancellationToken);
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
                    return PaymentOwnershipResult.Acquired(
                        proposedTerm,
                        leaseExpiresAtUtc,
                        round);
                }
                catch (PaymentDomainException)
                {
                    return PaymentOwnershipResult.NotAcquired(round);
                }
                catch (PaymentConcurrencyException)
                {
                    return PaymentOwnershipResult.NotAcquired(round);
                }
            }

            if (peerResults.Any(result =>
                    result.ErrorCode ==
                    PaymentCoordinationErrorCodes.ClaimLeaseActive))
            {
                return PaymentOwnershipResult.NotAcquired(round);
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
            return unavailable
                ? PaymentOwnershipResult.QuorumUnavailable(round)
                : PaymentOwnershipResult.NotAcquired(round);
        }

        return PaymentOwnershipResult.NotAcquired(MaximumRounds);
    }

    private async Task WaitForContentionTurnAsync(
        Guid paymentId,
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

        var slotMilliseconds = Math.Max(
            1,
            options.ScanIntervalMilliseconds / (orderedCandidates.Length + 1));
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
