using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh;

public interface IMeshPeerDirectory
{
    IReadOnlyCollection<MeshPeer> GetPeers();

    bool Contains(NodeId nodeId);
}
