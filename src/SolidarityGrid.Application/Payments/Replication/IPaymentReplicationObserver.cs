namespace SolidarityGrid.Application.Payments.Replication;

public interface IPaymentReplicationObserver
{
    void ReplicationStarted(
        Guid paymentId,
        string correlationId);

    void ReplicationCompleted(
        Guid paymentId,
        string correlationId,
        int successfulReplicas,
        bool quorumReached,
        TimeSpan duration);
}
