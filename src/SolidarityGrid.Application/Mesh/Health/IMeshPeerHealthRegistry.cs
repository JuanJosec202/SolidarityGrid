using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh.Health;

public interface IMeshPeerHealthRegistry
{
    IReadOnlyCollection<MeshPeerHealthSnapshot> GetSnapshots();

    MeshPeerHealthSnapshot? GetSnapshot(NodeId peerNodeId);

    MeshPeerHealthTransition RecordSuccess(
        MeshPeerProbeResult result,
        DateTimeOffset observedAtUtc);

    MeshPeerHealthTransition RecordFailure(
        MeshPeerProbeResult result,
        DateTimeOffset observedAtUtc);
}
