using Microsoft.Extensions.Logging;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Replication;

namespace SolidarityGrid.Infrastructure.Mesh;

public sealed class PaymentReplicationObserver(
    IMeshNodeIdentity localIdentity,
    ILogger<PaymentReplicationObserver> logger) : IPaymentReplicationObserver
{
    private const int RequiredRemoteReplicas = 1;

    public void ReplicationStarted(Guid paymentId, string correlationId)
    {
        using var scope = BeginScope(paymentId, correlationId);
        PaymentReplicationLog.Started(
            logger,
            paymentId,
            localIdentity.NodeId.Value);
    }

    public void ReplicationCompleted(
        Guid paymentId,
        string correlationId,
        int successfulReplicas,
        bool quorumReached,
        TimeSpan duration)
    {
        using var scope = BeginScope(paymentId, correlationId);
        if (quorumReached)
        {
            PaymentReplicationLog.QuorumReached(
                logger,
                paymentId,
                successfulReplicas,
                RequiredRemoteReplicas,
                duration.TotalMilliseconds);
        }
        else
        {
            PaymentReplicationLog.QuorumUnavailable(
                logger,
                paymentId,
                successfulReplicas,
                RequiredRemoteReplicas,
                duration.TotalMilliseconds);
        }
    }

    private IDisposable? BeginScope(Guid paymentId, string correlationId) =>
        logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = localIdentity.NodeId.Value,
            ["PaymentId"] = paymentId,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "PaymentReplication",
        });
}
