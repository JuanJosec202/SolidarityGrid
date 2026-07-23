using System.Collections.Concurrent;
using Grpc.Net.Client;
using SolidarityGrid.Application.Mesh;

namespace SolidarityGrid.Infrastructure.Mesh;

public sealed class GrpcMeshChannelPool :
    IDisposable
{
    private readonly IMeshPeerDirectory _peerDirectory;
    private readonly ConcurrentDictionary<string, Lazy<GrpcChannel>> _channels =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public GrpcMeshChannelPool(IMeshPeerDirectory peerDirectory)
    {
        _peerDirectory = peerDirectory;
    }

    public GrpcChannel GetChannel(MeshPeer peer)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(peer);

        var configuredPeer = _peerDirectory
            .GetPeers()
            .SingleOrDefault(candidate => candidate.NodeId == peer.NodeId);
        if (configuredPeer is null || configuredPeer.InternalUri != peer.InternalUri)
        {
            throw new InvalidOperationException(
                $"Peer '{peer.NodeId}' is not present in the configured mesh directory.");
        }

        var key = peer.InternalUri.AbsoluteUri;
        return _channels.GetOrAdd(
            key,
            _ => new Lazy<GrpcChannel>(
                () => CreateChannel(peer.InternalUri),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var channel in _channels.Values)
        {
            if (channel.IsValueCreated)
            {
                channel.Value.Dispose();
            }
        }

        _channels.Clear();
    }

    private static GrpcChannel CreateChannel(Uri address)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        };

        return GrpcChannel.ForAddress(
            address,
            new GrpcChannelOptions
            {
                HttpHandler = handler,
                DisposeHttpClient = true,
            });
    }
}
