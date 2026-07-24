using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed class PaymentProcessingOrchestrator(
    PaymentOwnershipCoordinator ownershipCoordinator,
    IPaymentCoordinationTransport transport,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IMeshNodeIdentity localIdentity,
    PaymentProcessingOptions options,
    IPaymentCoordinationObserver observer) : IPaymentProcessor
{
    public async Task<PaymentProcessingResult> ProcessAsync(
        Payment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        try
        {
            var ownership = await ownershipCoordinator.TryAcquireAsync(
                payment,
                correlationId,
                cancellationToken);
            if (ownership.Status != PaymentOwnershipStatus.Acquired)
            {
                return PaymentProcessingResult.From(
                    ownership.Status == PaymentOwnershipStatus.QuorumUnavailable
                        ? PaymentProcessingStatus.QuorumUnavailable
                        : PaymentProcessingStatus.NotAcquired,
                    mode: ownership.IsTakeover
                        ? PaymentProcessingMode.TakeoverProcessing
                        : null);
            }

            var term = ownership.Term!.Value;
            var mode = ownership.IsTakeover
                ? PaymentProcessingMode.TakeoverProcessing
                : PaymentProcessingMode.InitialProcessing;
            var startResult = await StartProcessingAsync(
                payment,
                term,
                correlationId,
                cancellationToken);
            if (!startResult.Succeeded)
            {
                Abort(payment, correlationId, term);
                return PaymentProcessingResult.From(
                    PaymentProcessingStatus.QuorumUnavailable,
                    term,
                    mode: mode);
            }

            if (ownership.IsTakeover)
            {
                observer.RecoveredPaymentProcessingStarted(
                    payment.Id,
                    correlationId,
                    ownership.PreviousOwnerNodeId!,
                    ownership.NewOwnerNodeId!,
                    ownership.PreviousTerm,
                    term,
                    payment.Attempt);
            }

            var renewalResult = await DelayWithRenewalsAsync(
                payment,
                term,
                correlationId,
                cancellationToken);
            if (!renewalResult.Succeeded)
            {
                Abort(payment, correlationId, term);
                return PaymentProcessingResult.From(
                    PaymentProcessingStatus.LeaseRenewalFailed,
                    term,
                    renewalResult.SuccessfulPeers,
                    renewalResult.Renewals,
                    mode);
            }

            var completed = await CompleteAsync(
                payment,
                term,
                correlationId,
                cancellationToken);
            if (!completed.Succeeded)
            {
                Abort(payment, correlationId, term);
                return PaymentProcessingResult.From(
                    PaymentProcessingStatus.CompletionQuorumUnavailable,
                    term,
                    completed.SuccessfulPeers,
                    renewalResult.Renewals,
                    mode);
            }

            if (ownership.IsTakeover)
            {
                observer.RecoveredPaymentCompleted(
                    payment.Id,
                    correlationId,
                    ownership.PreviousOwnerNodeId!,
                    ownership.NewOwnerNodeId!,
                    ownership.PreviousTerm,
                    term,
                    payment.Attempt);
            }

            return PaymentProcessingResult.From(
                PaymentProcessingStatus.AcquiredAndCompleted,
                term,
                completed.SuccessfulPeers,
                renewalResult.Renewals,
                mode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PaymentProcessingResult.From(PaymentProcessingStatus.Cancelled);
        }
    }

    private async Task<CoordinationStepResult> StartProcessingAsync(
        Payment payment,
        long term,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var occurredAtUtc = timeProvider.GetUtcNow();
        var command = new PaymentCoordinationCommand(
            payment.Id,
            localIdentity.NodeId,
            term,
            occurredAtUtc);
        var results = await transport.StartProcessingAsync(
            command,
            correlationId,
            cancellationToken);
        var successfulPeers = results.Count(result => result.Applied);
        if (successfulPeers == 0)
        {
            return CoordinationStepResult.Failed(successfulPeers);
        }

        var changed = payment.StartProcessing(
            localIdentity.NodeId,
            term,
            occurredAtUtc);
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        observer.ProcessingStarted(
            payment.Id,
            correlationId,
            localIdentity.NodeId.Value,
            term,
            payment.Attempt,
            successfulPeers);
        return CoordinationStepResult.Success(successfulPeers);
    }

    private async Task<RenewalResult> DelayWithRenewalsAsync(
        Payment payment,
        long term,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var duration = TimeSpan.FromMilliseconds(
            options.ProcessingDurationMilliseconds);
        var interval = TimeSpan.FromMilliseconds(
            options.LeaseRenewalIntervalMilliseconds);
        var startedAt = timeProvider.GetTimestamp();
        var renewals = 0;
        var lastSuccessfulPeers = 0;

        while (true)
        {
            var elapsed = timeProvider.GetElapsedTime(startedAt);
            var remaining = duration - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return RenewalResult.Success(lastSuccessfulPeers, renewals);
            }

            await Task.Delay(
                remaining < interval ? remaining : interval,
                timeProvider,
                cancellationToken);
            if (timeProvider.GetElapsedTime(startedAt) >= duration)
            {
                return RenewalResult.Success(lastSuccessfulPeers, renewals);
            }

            var occurredAtUtc = timeProvider.GetUtcNow();
            var leaseExpiresAtUtc = occurredAtUtc.AddMilliseconds(
                options.LeaseDurationMilliseconds);
            var command = new PaymentCoordinationCommand(
                payment.Id,
                localIdentity.NodeId,
                term,
                occurredAtUtc,
                leaseExpiresAtUtc);
            var results = await transport.RenewLeaseAsync(
                command,
                correlationId,
                cancellationToken);
            lastSuccessfulPeers = results.Count(result => result.Applied);
            if (lastSuccessfulPeers == 0)
            {
                return RenewalResult.Failed(lastSuccessfulPeers, renewals);
            }

            var changed = payment.RenewLease(
                localIdentity.NodeId,
                term,
                leaseExpiresAtUtc,
                occurredAtUtc);
            if (changed)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            renewals++;
            observer.LeaseRenewed(
                payment.Id,
                correlationId,
                localIdentity.NodeId.Value,
                term,
                lastSuccessfulPeers);
        }
    }

    private async Task<CoordinationStepResult> CompleteAsync(
        Payment payment,
        long term,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var occurredAtUtc = timeProvider.GetUtcNow();
        var command = new PaymentCoordinationCommand(
            payment.Id,
            localIdentity.NodeId,
            term,
            occurredAtUtc);
        var results = await transport.CompleteAsync(
            command,
            correlationId,
            cancellationToken);
        var successfulPeers = results.Count(result => result.Applied);
        if (successfulPeers == 0)
        {
            return CoordinationStepResult.Failed(successfulPeers);
        }

        observer.CompletionQuorumReached(
            payment.Id,
            correlationId,
            localIdentity.NodeId.Value,
            term,
            successfulPeers);
        var changed = payment.Complete(
            localIdentity.NodeId,
            term,
            occurredAtUtc);
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        observer.PaymentCompleted(
            payment.Id,
            correlationId,
            localIdentity.NodeId.Value,
            term,
            payment.Attempt);
        return CoordinationStepResult.Success(successfulPeers);
    }

    private void Abort(Payment payment, string correlationId, long term) =>
        observer.ProcessingAborted(
            payment.Id,
            correlationId,
            localIdentity.NodeId.Value,
            term,
            PaymentCoordinationErrorCodes.QuorumUnavailable);

    private sealed record CoordinationStepResult(bool Succeeded, int SuccessfulPeers)
    {
        public static CoordinationStepResult Success(int successfulPeers) =>
            new(true, successfulPeers);

        public static CoordinationStepResult Failed(int successfulPeers) =>
            new(false, successfulPeers);
    }

    private sealed record RenewalResult(
        bool Succeeded,
        int SuccessfulPeers,
        int Renewals)
    {
        public static RenewalResult Success(int successfulPeers, int renewals) =>
            new(true, successfulPeers, renewals);

        public static RenewalResult Failed(int successfulPeers, int renewals) =>
            new(false, successfulPeers, renewals);
    }
}
