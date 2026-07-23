using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh.Health;

public sealed class MeshPeerHealthMonitorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TwoSuccessfulPeersProduceTwoAliveTransitionsAndPropagateCorrelation()
    {
        var probe = new StubProbe(
        [
            InMemoryMeshPeerHealthRegistryTests.Success("node-b", Guid.NewGuid()),
            InMemoryMeshPeerHealthRegistryTests.Success("node-c", Guid.NewGuid()),
        ]);
        var registry = CreateRegistry();
        var monitor = new MeshPeerHealthMonitor(
            probe,
            registry,
            new FixedTimeProvider(Now));

        var transitions = await monitor.ObserveOnceAsync(
            "heartbeat-correlation",
            CancellationToken.None);

        Assert.Equal(2, transitions.Count);
        Assert.All(transitions, transition =>
            Assert.Equal(MeshPeerHealthStatus.Alive, transition.Current.Status));
        Assert.Equal("heartbeat-correlation", probe.LastCorrelationId);
        Assert.All(registry.GetSnapshots(), snapshot =>
            Assert.Equal(1, snapshot.ObservationVersion));
    }

    [Fact]
    public async Task PeerFailureDoesNotPreventSuccessfulPeer()
    {
        var probe = new StubProbe(
        [
            InMemoryMeshPeerHealthRegistryTests.Success("node-b", Guid.NewGuid()),
            InMemoryMeshPeerHealthRegistryTests.Failure(
                "node-c",
                MeshErrorCodes.DeadlineExceeded),
        ]);
        var registry = CreateRegistry();
        var monitor = new MeshPeerHealthMonitor(
            probe,
            registry,
            new FixedTimeProvider(Now.AddSeconds(4)));

        var transitions = await monitor.ObserveOnceAsync(
            "mixed",
            CancellationToken.None);

        Assert.Equal(2, transitions.Count);
        Assert.Equal(
            MeshPeerHealthStatus.Alive,
            registry.GetSnapshot(new NodeId("node-b"))!.Status);
        Assert.Equal(
            MeshPeerHealthStatus.Suspected,
            registry.GetSnapshot(new NodeId("node-c"))!.Status);
    }

    [Fact]
    public async Task TwoFailedPeersProduceTransitionsWithoutThrowing()
    {
        var registry = CreateRegistry();
        var monitor = new MeshPeerHealthMonitor(
            new StubProbe(
            [
                InMemoryMeshPeerHealthRegistryTests.Failure(
                    "node-b",
                    MeshErrorCodes.PeerUnreachable),
                InMemoryMeshPeerHealthRegistryTests.Failure(
                    "node-c",
                    MeshErrorCodes.RpcFailure),
            ]),
            registry,
            new FixedTimeProvider(Now.AddSeconds(4)));

        var transitions = await monitor.ObserveOnceAsync(
            "both-failed",
            CancellationToken.None);

        Assert.Equal(2, transitions.Count);
        Assert.All(
            transitions,
            transition => Assert.Equal(
                MeshPeerHealthStatus.Suspected,
                transition.Current.Status));
    }

    [Fact]
    public async Task ImmediateFailureBecomesUnreachable()
    {
        var registry = CreateRegistry();
        var monitor = new MeshPeerHealthMonitor(
            new StubProbe(
            [
                InMemoryMeshPeerHealthRegistryTests.Failure(
                    "node-b",
                    MeshErrorCodes.ProtocolMismatch),
            ]),
            registry,
            new FixedTimeProvider(Now));

        await monitor.ObserveOnceAsync("immediate", CancellationToken.None);

        Assert.Equal(
            MeshPeerHealthStatus.Unreachable,
            registry.GetSnapshot(new NodeId("node-b"))!.Status);
    }

    [Fact]
    public async Task CancelledResultIsNotRecorded()
    {
        var registry = CreateRegistry();
        var monitor = new MeshPeerHealthMonitor(
            new StubProbe(
            [
                InMemoryMeshPeerHealthRegistryTests.Failure(
                    "node-b",
                    MeshErrorCodes.Cancelled),
            ]),
            registry,
            new FixedTimeProvider(Now));

        var transitions = await monitor.ObserveOnceAsync(
            "cancelled-result",
            CancellationToken.None);

        Assert.Empty(transitions);
        Assert.Equal(
            0,
            registry.GetSnapshot(new NodeId("node-b"))!.ObservationVersion);
    }

    [Fact]
    public async Task GlobalCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var monitor = new MeshPeerHealthMonitor(
            new CancellingProbe(),
            CreateRegistry(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => monitor.ObserveOnceAsync("cancel", cancellation.Token));
    }

    private static InMemoryMeshPeerHealthRegistry CreateRegistry() =>
        new(
            [
                new MeshPeer(new NodeId("node-b"), new Uri("http://node-b:8081")),
                new MeshPeer(new NodeId("node-c"), new Uri("http://node-c:8081")),
            ],
            Now,
            new MeshFailureDetectionThresholds(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5)));

    private sealed class StubProbe(
        IReadOnlyCollection<MeshPeerProbeResult> results) : IMeshPeerProbe
    {
        public string? LastCorrelationId { get; private set; }

        public Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCorrelationId = correlationId;
            return Task.FromResult(results);
        }
    }

    private sealed class CancellingProbe : IMeshPeerProbe
    {
        public Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
            string correlationId,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<IReadOnlyCollection<MeshPeerProbeResult>>(
                cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
