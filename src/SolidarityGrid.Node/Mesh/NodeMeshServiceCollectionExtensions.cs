using SolidarityGrid.Application.Mesh;

namespace SolidarityGrid.Node.Mesh;

public static class NodeMeshServiceCollectionExtensions
{
    public static IServiceCollection AddNodeMesh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMeshNodeIdentity, ConfiguredMeshNodeIdentity>();
        services.AddSingleton<IMeshPeerDirectory, ConfiguredMeshPeerDirectory>();
        return services;
    }
}
