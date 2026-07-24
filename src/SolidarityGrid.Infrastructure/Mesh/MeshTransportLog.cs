using Microsoft.Extensions.Logging;

namespace SolidarityGrid.Infrastructure.Mesh;

internal static partial class MeshTransportLog
{
    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Debug,
        EventName = "MeshProbeStarted",
        Message = "Mesh probe from {LocalNodeId} ({LocalInstanceId}) to {PeerNodeId} started using protocol {ProtocolVersion}.")]
    public static partial void ProbeStarted(
        ILogger logger,
        string localNodeId,
        Guid localInstanceId,
        string peerNodeId,
        int protocolVersion);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Debug,
        EventName = "MeshProbeSucceeded",
        Message = "Mesh probe to {PeerNodeId} ({PeerInstanceId}) succeeded in {DurationMilliseconds} ms using protocol {ProtocolVersion}.")]
    public static partial void ProbeSucceeded(
        ILogger logger,
        string peerNodeId,
        Guid peerInstanceId,
        double durationMilliseconds,
        int protocolVersion);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Warning,
        EventName = "MeshProbeFailed",
        Message = "Mesh probe to {PeerNodeId} failed in {DurationMilliseconds} ms with {ErrorCode} and gRPC status {GrpcStatus}.")]
    public static partial void ProbeFailed(
        ILogger logger,
        string peerNodeId,
        double durationMilliseconds,
        string errorCode,
        string grpcStatus);

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Warning,
        EventName = "MeshPeerIdentityMismatch",
        Message = "Mesh peer {PeerNodeId} returned identity {RemoteNodeId} ({PeerInstanceId}) using protocol {ProtocolVersion}.")]
    public static partial void PeerIdentityMismatch(
        ILogger logger,
        string peerNodeId,
        string remoteNodeId,
        string peerInstanceId,
        int protocolVersion);
}
