using System.Collections.ObjectModel;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh.Health;

public sealed class InMemoryMeshPeerHealthRegistry : IMeshPeerHealthRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<NodeId, MeshPeerHealthSnapshot> _snapshots;
    private readonly MeshFailureDetectionThresholds _thresholds;

    public InMemoryMeshPeerHealthRegistry(
        IReadOnlyCollection<MeshPeer> peers,
        DateTimeOffset monitoringStartedAtUtc,
        MeshFailureDetectionThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (thresholds.SuspectAfter <= TimeSpan.Zero ||
            thresholds.UnreachableAfter <= thresholds.SuspectAfter)
        {
            throw new ArgumentException(
                "Mesh failure detection thresholds are invalid.",
                nameof(thresholds));
        }

        _thresholds = thresholds;
        _snapshots = peers.ToDictionary(
            peer => peer.NodeId,
            peer => new MeshPeerHealthSnapshot(
                peer.NodeId,
                peer.InternalUri,
                MeshPeerHealthStatus.Unknown,
                null,
                monitoringStartedAtUtc,
                null,
                null,
                null,
                monitoringStartedAtUtc,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                0));
    }

    public IReadOnlyCollection<MeshPeerHealthSnapshot> GetSnapshots()
    {
        lock (_gate)
        {
            return new ReadOnlyCollection<MeshPeerHealthSnapshot>(
                _snapshots.Values
                    .OrderBy(snapshot => snapshot.PeerNodeId.Value, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public MeshPeerHealthSnapshot? GetSnapshot(NodeId peerNodeId)
    {
        ArgumentNullException.ThrowIfNull(peerNodeId);
        lock (_gate)
        {
            return _snapshots.GetValueOrDefault(peerNodeId);
        }
    }

    public MeshPeerHealthTransition RecordSuccess(
        MeshPeerProbeResult result,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsReachable ||
            !result.RemoteInstanceId.HasValue ||
            result.RemoteInstanceId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A successful mesh observation requires a valid InstanceId.",
                nameof(result));
        }

        lock (_gate)
        {
            var previous = GetRequired(result.NodeId);
            var newInstanceId = result.RemoteInstanceId!.Value;
            var restartDetected = previous.InstanceId.HasValue &&
                                  previous.InstanceId.Value != newInstanceId;
            var recovered = previous.Status is MeshPeerHealthStatus.Suspected or
                MeshPeerHealthStatus.Unreachable;
            var current = previous with
            {
                Status = MeshPeerHealthStatus.Alive,
                InstanceId = newInstanceId,
                LastProbeStartedAtUtc = ProbeStartedAt(result, observedAtUtc),
                LastSuccessfulProbeAtUtc = observedAtUtc,
                StatusChangedAtUtc = previous.Status == MeshPeerHealthStatus.Alive
                    ? previous.StatusChangedAtUtc
                    : observedAtUtc,
                ConsecutiveFailures = 0,
                TotalSuccessfulProbes = previous.TotalSuccessfulProbes + 1,
                RestartCount = previous.RestartCount + (restartDetected ? 1 : 0),
                LastDuration = result.Duration,
                LastErrorCode = null,
                LastErrorMessage = null,
                ObservationVersion = previous.ObservationVersion + 1,
            };
            _snapshots[result.NodeId] = current;
            return Transition(previous, current, recovered, restartDetected);
        }
    }

    public MeshPeerHealthTransition RecordFailure(
        MeshPeerProbeResult result,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            var previous = GetRequired(result.NodeId);
            if (MeshFailureClassification.IsCancelled(result.ErrorCode))
            {
                return Transition(previous, previous, false, false);
            }

            var elapsed = observedAtUtc -
                (previous.LastSuccessfulProbeAtUtc ??
                 previous.MonitoringStartedAtUtc);
            var status = DetermineFailureStatus(previous.Status, result.ErrorCode, elapsed);
            var current = previous with
            {
                Status = status,
                LastProbeStartedAtUtc = ProbeStartedAt(result, observedAtUtc),
                LastFailedProbeAtUtc = observedAtUtc,
                StatusChangedAtUtc = status == previous.Status
                    ? previous.StatusChangedAtUtc
                    : observedAtUtc,
                ConsecutiveFailures = previous.ConsecutiveFailures + 1,
                TotalFailedProbes = previous.TotalFailedProbes + 1,
                LastDuration = result.Duration,
                LastErrorCode = result.ErrorCode,
                LastErrorMessage = result.ErrorMessage,
                ObservationVersion = previous.ObservationVersion + 1,
            };
            _snapshots[result.NodeId] = current;
            return Transition(previous, current, false, false);
        }
    }

    private MeshPeerHealthStatus DetermineFailureStatus(
        MeshPeerHealthStatus previousStatus,
        string? errorCode,
        TimeSpan elapsed)
    {
        if (MeshFailureClassification.IsImmediate(errorCode) ||
            elapsed >= _thresholds.UnreachableAfter)
        {
            return MeshPeerHealthStatus.Unreachable;
        }

        if (elapsed >= _thresholds.SuspectAfter)
        {
            return MeshPeerHealthStatus.Suspected;
        }

        return previousStatus;
    }

    private MeshPeerHealthSnapshot GetRequired(NodeId nodeId) =>
        _snapshots.TryGetValue(nodeId, out var snapshot)
            ? snapshot
            : throw new KeyNotFoundException(
                $"Peer '{nodeId}' is not registered for mesh monitoring.");

    private static DateTimeOffset ProbeStartedAt(
        MeshPeerProbeResult result,
        DateTimeOffset observedAtUtc) =>
        observedAtUtc - result.Duration;

    private static MeshPeerHealthTransition Transition(
        MeshPeerHealthSnapshot previous,
        MeshPeerHealthSnapshot current,
        bool recovered,
        bool restartDetected) =>
        new(
            previous,
            current,
            previous.Status != current.Status,
            recovered,
            restartDetected,
            previous.InstanceId,
            current.InstanceId);
}
