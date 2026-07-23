using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Configuration;

namespace SolidarityGrid.Node.Mesh;

public sealed class ConfiguredMeshNodeIdentity : IMeshNodeIdentity
{
    public ConfiguredMeshNodeIdentity(
        IOptions<NodeOptions> nodeOptions,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(nodeOptions);
        ArgumentNullException.ThrowIfNull(idGenerator);

        NodeId = new NodeId(nodeOptions.Value.NodeId);
        InstanceId = idGenerator.NewId();
        if (InstanceId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The generated mesh InstanceId cannot be empty.");
        }
    }

    public NodeId NodeId { get; }

    public Guid InstanceId { get; }
}
