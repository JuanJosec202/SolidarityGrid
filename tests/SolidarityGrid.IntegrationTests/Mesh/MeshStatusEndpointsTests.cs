using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Mesh;

public sealed class MeshStatusEndpointsTests
{
    [Fact]
    public async Task StatusReturnsCachedIdentityPeersCountersAndCorrelation()
    {
        using var factory = new StatusFactory(
            MeshPeerHealthStatus.Alive,
            MeshPeerHealthStatus.Suspected);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mesh/status");
        request.Headers.Add("X-Correlation-ID", "cached-status-correlation");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-node", root.GetProperty("nodeId").GetString());
        Assert.NotEqual(
            Guid.Empty,
            root.GetProperty("instanceId").GetGuid());
        Assert.True(root.TryGetProperty("observedAtUtc", out _));
        var peers = root.GetProperty("peers");
        Assert.Equal(2, peers.GetArrayLength());
        Assert.Equal("Alive", peers[0].GetProperty("status").GetString());
        Assert.Equal("Suspected", peers[1].GetProperty("status").GetString());
        Assert.Equal(7, peers[0].GetProperty("totalSuccessfulProbes").GetInt64());
        Assert.Equal(2, peers[1].GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(1, peers[0].GetProperty("restartCount").GetInt64());
        Assert.True(peers[0].TryGetProperty("lastSuccessfulProbeAtUtc", out _));
        Assert.Equal(0, factory.Probe.CallCount);
        Assert.Equal(
            "cached-status-correlation",
            response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.DoesNotContain("RpcException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MeshPeerHealthStatus.Unknown, "Unknown")]
    [InlineData(MeshPeerHealthStatus.Unreachable, "Unreachable")]
    public async Task StatusSerializesHealthAsText(
        MeshPeerHealthStatus status,
        string expected)
    {
        using var factory = new StatusFactory(status, status);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/mesh/status");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(
            document.RootElement.GetProperty("peers").EnumerateArray(),
            peer => Assert.Equal(
                expected,
                peer.GetProperty("status").GetString()));
        Assert.Equal(0, factory.Probe.CallCount);
    }

    [Fact]
    public async Task PeerHealthDoesNotAffectReadiness()
    {
        using var factory = new StatusFactory(
            MeshPeerHealthStatus.Unreachable,
            MeshPeerHealthStatus.Unreachable);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StatusFactory : ValidNodeFactory
    {
        private readonly ControlledRegistry _registry;

        public StatusFactory(
            MeshPeerHealthStatus first,
            MeshPeerHealthStatus second)
        {
            _registry = new ControlledRegistry(first, second);
        }

        public CountingProbe Probe { get; } = new();

        protected override void ConfigureWebHost(
            Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMeshPeerHealthRegistry>();
                services.AddSingleton<IMeshPeerHealthRegistry>(_registry);
                services.RemoveAll<IMeshPeerProbe>();
                services.AddSingleton<IMeshPeerProbe>(Probe);
                var heartbeat = services.FirstOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType ==
                    typeof(MeshHeartbeatBackgroundService));
                if (heartbeat is not null)
                {
                    services.Remove(heartbeat);
                }
            });
        }
    }

    private sealed class CountingProbe : IMeshPeerProbe
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyCollection<MeshPeerProbeResult>>([]);
        }
    }

    private sealed class ControlledRegistry : IMeshPeerHealthRegistry
    {
        private readonly IReadOnlyCollection<MeshPeerHealthSnapshot> _snapshots;

        public ControlledRegistry(
            MeshPeerHealthStatus first,
            MeshPeerHealthStatus second)
        {
            var now = new DateTimeOffset(
                2026,
                7,
                24,
                15,
                0,
                0,
                TimeSpan.Zero);
            _snapshots = Array.AsReadOnly(
            [
                Snapshot("peer-one", first, now, 1),
                Snapshot("peer-two", second, now, 0),
            ]);
        }

        public IReadOnlyCollection<MeshPeerHealthSnapshot> GetSnapshots() =>
            _snapshots;

        public MeshPeerHealthSnapshot? GetSnapshot(NodeId peerNodeId) =>
            _snapshots.SingleOrDefault(item => item.PeerNodeId == peerNodeId);

        public MeshPeerHealthTransition RecordSuccess(
            MeshPeerProbeResult result,
            DateTimeOffset observedAtUtc) =>
            throw new InvalidOperationException("The cached endpoint must not write.");

        public MeshPeerHealthTransition RecordFailure(
            MeshPeerProbeResult result,
            DateTimeOffset observedAtUtc) =>
            throw new InvalidOperationException("The cached endpoint must not write.");

        private static MeshPeerHealthSnapshot Snapshot(
            string nodeId,
            MeshPeerHealthStatus status,
            DateTimeOffset now,
            long restartCount) =>
            new(
                new NodeId(nodeId),
                new Uri($"http://{nodeId}:8081"),
                status,
                status == MeshPeerHealthStatus.Unknown ? null : Guid.NewGuid(),
                now.AddMinutes(-1),
                now.AddSeconds(-1),
                now.AddSeconds(-2),
                status is MeshPeerHealthStatus.Suspected or
                    MeshPeerHealthStatus.Unreachable
                    ? now.AddSeconds(-1)
                    : null,
                now.AddSeconds(-3),
                status == MeshPeerHealthStatus.Suspected ? 2 : 0,
                7,
                status == MeshPeerHealthStatus.Suspected ? 2 : 0,
                restartCount,
                TimeSpan.FromMilliseconds(12),
                status == MeshPeerHealthStatus.Suspected
                    ? MeshErrorCodes.DeadlineExceeded
                    : null,
                status == MeshPeerHealthStatus.Suspected
                    ? "The mesh probe deadline was exceeded."
                    : null,
                9);
    }
}
