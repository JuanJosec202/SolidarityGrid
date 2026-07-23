namespace SolidarityGrid.Application.Payments.Replication;

public interface IPaymentReplicaTransport
{
    Task<IReadOnlyCollection<PaymentReplicaResult>> ReplicateAsync(
        PaymentReplica replica,
        string correlationId,
        CancellationToken cancellationToken);
}
