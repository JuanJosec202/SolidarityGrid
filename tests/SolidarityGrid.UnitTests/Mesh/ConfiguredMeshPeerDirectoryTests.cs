using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Configuration;
using SolidarityGrid.Node.Mesh;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh;

public sealed class ConfiguredMeshPeerDirectoryTests
{
    [Fact]
    public void ReturnsExactlyTwoConfiguredPeersWithInternalUris()
    {
        var directory = CreateDirectory();

        var peers = directory.GetPeers().ToArray();

        Assert.Equal(2, peers.Length);
        Assert.Equal("node-b", peers[0].NodeId.Value);
        Assert.Equal(new Uri("http://node-b:8081"), peers[0].InternalUri);
        Assert.Equal("node-c", peers[1].NodeId.Value);
        Assert.Equal(new Uri("http://node-c:8081"), peers[1].InternalUri);
    }

    [Fact]
    public void ReturnedCollectionIsReadOnly()
    {
        var peers = Assert.IsAssignableFrom<ICollection<MeshPeer>>(
            CreateDirectory().GetPeers());

        Assert.True(peers.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => peers.Add(new(
                new NodeId("node-d"),
                new Uri("http://node-d:8081"))));
    }

    [Fact]
    public void DirectoryDoesNotContainLocalNode()
    {
        Assert.False(CreateDirectory().Contains(new NodeId("node-a")));
    }

    [Fact]
    public void ExistingPeerCanBeFound()
    {
        Assert.True(CreateDirectory().Contains(new NodeId("node-b")));
    }

    [Fact]
    public void UnknownPeerCannotBeFound()
    {
        Assert.False(CreateDirectory().Contains(new NodeId("node-z")));
    }

    [Fact]
    public void LocalPeerIsRejected()
    {
        var options = CreateOptions(
            new PeerOptions
            {
                NodeId = "node-a",
                Url = "http://node-a:8081",
            });

        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredMeshPeerDirectory(options));
    }

    [Fact]
    public void DuplicatePeerIsRejected()
    {
        var options = CreateOptions(
            new PeerOptions
            {
                NodeId = "node-b",
                Url = "http://node-b:8081",
            },
            new PeerOptions
            {
                NodeId = "NODE-B",
                Url = "http://node-b:8081",
            });

        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredMeshPeerDirectory(options));
    }

    [Fact]
    public void RelativePeerUriIsRejected()
    {
        var options = CreateOptions(
            new PeerOptions
            {
                NodeId = "node-b",
                Url = "/mesh",
            });

        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredMeshPeerDirectory(options));
    }

    private static ConfiguredMeshPeerDirectory CreateDirectory() =>
        new(CreateOptions(
            new PeerOptions
            {
                NodeId = "node-b",
                Url = "http://node-b:8081",
            },
            new PeerOptions
            {
                NodeId = "node-c",
                Url = "http://node-c:8081",
            }));

    private static IOptions<NodeOptions> CreateOptions(params PeerOptions[] peers) =>
        Options.Create(new NodeOptions
        {
            NodeId = "node-a",
            PublicUrl = "http://localhost:5101",
            InternalUrl = "http://node-a:8081",
            Environment = "Test",
            Peers = peers,
        });
}
