using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh;

public sealed class GrpcMeshChannelPoolTests
{
    [Fact]
    public void ReusesChannelForConfiguredPeer()
    {
        var peer = new MeshPeer(
            new NodeId("node-b"),
            new Uri("http://node-b:8081"));
        using var pool = new GrpcMeshChannelPool(
            new StubDirectory([peer]));

        var first = pool.GetChannel(peer);
        var second = pool.GetChannel(peer);

        Assert.Same(first, second);
    }

    [Fact]
    public void RejectsPeerOutsideDirectory()
    {
        var configured = new MeshPeer(
            new NodeId("node-b"),
            new Uri("http://node-b:8081"));
        var unknown = new MeshPeer(
            new NodeId("node-c"),
            new Uri("http://node-c:8081"));
        using var pool = new GrpcMeshChannelPool(
            new StubDirectory([configured]));

        Assert.Throws<InvalidOperationException>(() => pool.GetChannel(unknown));
    }

    [Fact]
    public void CannotCreateChannelsAfterDisposal()
    {
        var peer = new MeshPeer(
            new NodeId("node-b"),
            new Uri("http://node-b:8081"));
        var pool = new GrpcMeshChannelPool(new StubDirectory([peer]));

        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pool.GetChannel(peer));
    }

    private sealed class StubDirectory(
        IReadOnlyCollection<MeshPeer> peers) : IMeshPeerDirectory
    {
        public IReadOnlyCollection<MeshPeer> GetPeers() => peers;

        public bool Contains(NodeId nodeId) =>
            peers.Any(peer => peer.NodeId == nodeId);
    }
}
