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
}
