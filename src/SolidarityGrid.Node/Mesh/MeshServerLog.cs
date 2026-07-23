namespace SolidarityGrid.Node.Mesh;

internal static partial class MeshServerLog
{
    [LoggerMessage(
        EventId = 4200,
        Level = LogLevel.Information,
        EventName = "MeshProbeReceived",
        Message = "Mesh probe from {PeerNodeId} ({PeerInstanceId}) was received by {LocalNodeId} ({LocalInstanceId}) using protocol {ProtocolVersion}.")]
    public static partial void ProbeReceived(
        ILogger logger,
        string peerNodeId,
        Guid peerInstanceId,
        string localNodeId,
        Guid localInstanceId,
        int protocolVersion);

    [LoggerMessage(
        EventId = 4201,
        Level = LogLevel.Warning,
        EventName = "MeshProbeRejected",
        Message = "Mesh probe from {PeerNodeId} was rejected by {LocalNodeId}: {Reason} ({GrpcStatus}).")]
    public static partial void ProbeRejected(
        ILogger logger,
        string peerNodeId,
        string localNodeId,
        string reason,
        string grpcStatus);

    [LoggerMessage(
        EventId = 4202,
        Level = LogLevel.Information,
        EventName = "MeshProbeResponded",
        Message = "Mesh probe response from {LocalNodeId} ({LocalInstanceId}) to {PeerNodeId} completed using protocol {ProtocolVersion}.")]
    public static partial void ProbeResponded(
        ILogger logger,
        string localNodeId,
        Guid localInstanceId,
        string peerNodeId,
        int protocolVersion);

    [LoggerMessage(
        EventId = 4203,
        Level = LogLevel.Information,
        EventName = "PaymentReplicaStored",
        Message = "Payment {PaymentId} replica from {PeerNodeId} was durably stored by {LocalNodeId} as {Status} version {Version}.")]
    public static partial void ReplicaStored(
        ILogger logger,
        Guid paymentId,
        string peerNodeId,
        string localNodeId,
        string status,
        long version);

    [LoggerMessage(
        EventId = 4204,
        Level = LogLevel.Information,
        EventName = "PaymentReplicaAlreadyExists",
        Message = "Payment {PaymentId} replica from {PeerNodeId} already existed on {LocalNodeId} as {Status} version {Version}.")]
    public static partial void ReplicaAlreadyExists(
        ILogger logger,
        Guid paymentId,
        string peerNodeId,
        string localNodeId,
        string status,
        long version);

    [LoggerMessage(
        EventId = 4205,
        Level = LogLevel.Warning,
        EventName = "PaymentReplicaRejected",
        Message = "Payment {PaymentId} replica from {PeerNodeId} was rejected by {LocalNodeId} with {ErrorCode} ({GrpcStatus}).")]
    public static partial void ReplicaRejected(
        ILogger logger,
        Guid paymentId,
        string peerNodeId,
        string localNodeId,
        string errorCode,
        string grpcStatus);
}
