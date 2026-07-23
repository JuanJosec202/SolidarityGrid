using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh;

public sealed record MeshPeer(NodeId NodeId, Uri InternalUri);
