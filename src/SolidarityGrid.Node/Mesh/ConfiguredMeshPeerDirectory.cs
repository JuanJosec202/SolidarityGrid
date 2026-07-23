using System.Collections.ObjectModel;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Configuration;

namespace SolidarityGrid.Node.Mesh;

public sealed class ConfiguredMeshPeerDirectory : IMeshPeerDirectory
{
    private readonly ReadOnlyCollection<MeshPeer> _peers;
    private readonly HashSet<NodeId> _peerIds;

    public ConfiguredMeshPeerDirectory(IOptions<NodeOptions> nodeOptions)
    {
        ArgumentNullException.ThrowIfNull(nodeOptions);

        var localNodeId = new NodeId(nodeOptions.Value.NodeId);
        var peers = new List<MeshPeer>(nodeOptions.Value.Peers.Count);
        _peerIds = [];

        foreach (var configuredPeer in nodeOptions.Value.Peers)
        {
            var peerNodeId = new NodeId(configuredPeer.NodeId);
            if (peerNodeId == localNodeId)
            {
                throw new InvalidOperationException(
                    $"Mesh peer '{peerNodeId}' cannot be the local node.");
            }

            if (!_peerIds.Add(peerNodeId))
            {
                throw new InvalidOperationException(
                    $"Mesh peer '{peerNodeId}' is duplicated.");
            }

            if (!Uri.TryCreate(
                    configuredPeer.Url,
                    UriKind.Absolute,
                    out var internalUri) ||
                (internalUri.Scheme != Uri.UriSchemeHttp &&
                 internalUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Mesh peer '{peerNodeId}' must have an absolute HTTP or HTTPS URI.");
            }

            peers.Add(new MeshPeer(peerNodeId, internalUri));
        }

        _peers = peers.AsReadOnly();
    }

    public IReadOnlyCollection<MeshPeer> GetPeers() => _peers;

    public bool Contains(NodeId nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return _peerIds.Contains(nodeId);
    }
}
