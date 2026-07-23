using Microsoft.Extensions.Logging;

namespace SolidarityGrid.Infrastructure.Mesh;

internal static partial class PaymentReplicationLog
{
    [LoggerMessage(4400, LogLevel.Information, "Payment {PaymentId} replication started from {LocalNodeId}.", EventName = "PaymentReplicationStarted")]
    public static partial void Started(ILogger logger, Guid paymentId, string localNodeId);

    [LoggerMessage(4401, LogLevel.Information, "Payment {PaymentId} replica was stored by {PeerNodeId}. AlreadyExisted={AlreadyExisted}.", EventName = "PaymentReplicaStored")]
    public static partial void Stored(ILogger logger, Guid paymentId, string peerNodeId, bool alreadyExisted);

    [LoggerMessage(4402, LogLevel.Information, "Payment {PaymentId} replica already existed on {PeerNodeId}.", EventName = "PaymentReplicaAlreadyExists")]
    public static partial void AlreadyExists(ILogger logger, Guid paymentId, string peerNodeId);

    [LoggerMessage(4403, LogLevel.Warning, "Payment {PaymentId} replica was rejected or unavailable on {PeerNodeId} with {ErrorCode}.", EventName = "PaymentReplicaRejected")]
    public static partial void Rejected(ILogger logger, Guid paymentId, string peerNodeId, string errorCode);

    [LoggerMessage(4404, LogLevel.Information, "Payment {PaymentId} reached replication quorum with {SuccessfulReplicas}/{RequiredReplicas} remote replicas in {DurationMilliseconds} ms.", EventName = "PaymentReplicationQuorumReached")]
    public static partial void QuorumReached(ILogger logger, Guid paymentId, int successfulReplicas, int requiredReplicas, double durationMilliseconds);

    [LoggerMessage(4405, LogLevel.Warning, "Payment {PaymentId} could not reach replication quorum: {SuccessfulReplicas}/{RequiredReplicas} remote replicas in {DurationMilliseconds} ms.", EventName = "PaymentReplicationQuorumUnavailable")]
    public static partial void QuorumUnavailable(ILogger logger, Guid paymentId, int successfulReplicas, int requiredReplicas, double durationMilliseconds);
}
