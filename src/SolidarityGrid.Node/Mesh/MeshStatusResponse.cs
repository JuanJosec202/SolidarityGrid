namespace SolidarityGrid.Node.Mesh;

public sealed record MeshStatusResponse(
    string NodeId,
    Guid InstanceId,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyCollection<MeshPeerStatusResponse> Peers);

public sealed record MeshPeerStatusResponse(
    string NodeId,
    string InternalUrl,
    string Status,
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
    double? LastDurationMilliseconds,
    string? LastErrorCode,
    string? LastErrorMessage,
    long ObservationVersion);
