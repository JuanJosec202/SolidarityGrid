using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh;

public sealed record MeshPeerProbeResult(
    NodeId NodeId,
    Uri InternalUri,
    bool IsReachable,
    NodeId? RemoteNodeId,
    Guid? RemoteInstanceId,
    int? ProtocolVersion,
    TimeSpan Duration,
    string? ErrorCode,
    string? ErrorMessage);
