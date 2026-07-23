using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Mesh;

public interface IMeshNodeIdentity
{
    NodeId NodeId { get; }

    Guid InstanceId { get; }
}
