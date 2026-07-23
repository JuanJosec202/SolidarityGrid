using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Replication;

public sealed class PaymentReplicationCoordinator(
    IPaymentReplicaTransport replicaTransport,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IPaymentReplicationObserver observer)
{
    private const int RequiredRemoteReplicas = 1;

    public async Task<PaymentReplicationOutcome> EnsureReplicatedAsync(
        Payment payment,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        if (payment.Status != PaymentStatus.Received)
        {
            return PaymentReplicationOutcome.AlreadyReplicated();
        }

        var startedTimestamp = timeProvider.GetTimestamp();
        var replicatedAtUtc = timeProvider.GetUtcNow();
        observer.ReplicationStarted(payment.Id, correlationId);
        var replica = new PaymentReplica(
            payment.Id,
            payment.IdempotencyKey,
            payment.Amount,
            payment.CreatedAtUtc,
            replicatedAtUtc);
        var results = await replicaTransport.ReplicateAsync(
            replica,
            correlationId,
            cancellationToken);
        var successfulReplicas = results.Count(result => result.Stored);
        var quorumReached = successfulReplicas >= RequiredRemoteReplicas;

        if (quorumReached && payment.MarkReplicated(replicatedAtUtc))
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (PaymentConcurrencyException exception)
                when (exception.PaymentId == payment.Id &&
                      exception.ActualVersion >= 2)
            {
                // Another request durably completed the same transition.
            }
        }

        observer.ReplicationCompleted(
            payment.Id,
            correlationId,
            successfulReplicas,
            quorumReached,
            timeProvider.GetElapsedTime(startedTimestamp));

        return quorumReached
            ? PaymentReplicationOutcome.Reached(replicatedAtUtc, results)
            : PaymentReplicationOutcome.Unavailable(replicatedAtUtc, results);
    }
}
