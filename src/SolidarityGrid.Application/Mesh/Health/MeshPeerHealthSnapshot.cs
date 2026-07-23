using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh.Health;

public sealed record MeshPeerHealthSnapshot(
    NodeId PeerNodeId,
    Uri InternalUri,
    MeshPeerHealthStatus Status,
    Guid? InstanceId,
    DateTimeOffset MonitoringStartedAtUtc,
    DateTimeOffset? LastProbeStartedAtUtc,
    DateTimeOffset? LastSuccessfulProbeAtUtc,
    DateTimeOffset? LastFailedProbeAtUtc,
    DateTimeOffset StatusChangedAtUtc,
    int ConsecutiveFailures,
    long TotalSuccessfulProbes,
    long TotalFailedProbes,
    long RestartCount,
    TimeSpan? LastDuration,
    string? LastErrorCode,
    string? LastErrorMessage,
    long ObservationVersion);
