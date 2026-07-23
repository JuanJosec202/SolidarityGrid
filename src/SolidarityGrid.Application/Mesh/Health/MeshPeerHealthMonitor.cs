namespace SolidarityGrid.Application.Mesh.Health;

public sealed class MeshPeerHealthMonitor(
    IMeshPeerProbe peerProbe,
    IMeshPeerHealthRegistry registry,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<MeshPeerHealthTransition>> ObserveOnceAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var results = await peerProbe.ProbeAllAsync(correlationId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var transitions = new List<MeshPeerHealthTransition>(results.Count);
        foreach (var result in results)
        {
            if (MeshFailureClassification.IsCancelled(result.ErrorCode))
            {
                continue;
            }

            var observedAtUtc = timeProvider.GetUtcNow();
            transitions.Add(result.IsReachable
                ? registry.RecordSuccess(result, observedAtUtc)
                : registry.RecordFailure(result, observedAtUtc));
        }

        return transitions.AsReadOnly();
    }
}
