using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh;
using SolidarityGrid.Infrastructure.Mesh.Configuration;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh.Health;

public sealed class MeshHeartbeatBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteCycleRunsInitialObservationWithInternalCorrelation()
    {
        var probe = new SequencedProbe();
        var service = CreateService(probe);

        var transitions = await service.ExecuteCycleAsync(CancellationToken.None);

        Assert.Single(transitions);
        Assert.StartsWith(
            "mesh-heartbeat-",
            probe.LastCorrelationId,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PeriodicExecutionDoesNotOverlapCyclesAndCancelsCleanly()
    {
        var probe = new SequencedProbe(targetCalls: 3);
        var service = CreateService(probe);

        await service.StartAsync(CancellationToken.None);
        await probe.TargetReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);
        service.Dispose();

        Assert.True(probe.CallCount >= 3);
        Assert.Equal(1, probe.MaximumConcurrentCalls);
    }

    [Fact]
    public async Task UnexpectedExceptionDoesNotPreventNextCycle()
    {
        var probe = new SequencedProbe(targetCalls: 2, throwOnFirstCall: true);
        var service = CreateService(probe);

        await service.StartAsync(CancellationToken.None);
        await probe.TargetReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);
        service.Dispose();

        Assert.True(probe.CallCount >= 2);
        Assert.Equal(1, probe.MaximumConcurrentCalls);
    }

    [Fact]
    public async Task FailedPeerDoesNotStopPeriodicService()
    {
        var probe = new SequencedProbe(targetCalls: 3, returnFailure: true);
        var service = CreateService(probe);

        await service.StartAsync(CancellationToken.None);
        await probe.TargetReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);
        service.Dispose();

        Assert.True(probe.CallCount >= 3);
        Assert.Equal(1, probe.MaximumConcurrentCalls);
    }

    private static MeshHeartbeatBackgroundService CreateService(IMeshPeerProbe probe)
    {
        var started = DateTimeOffset.UtcNow;
        var peer = new MeshPeer(
            new NodeId("node-b"),
            new Uri("http://node-b:8081"));
        var registry = new InMemoryMeshPeerHealthRegistry(
            [peer],
            started,
            new MeshFailureDetectionThresholds(
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(3)));
        var monitor = new MeshPeerHealthMonitor(
            probe,
            registry,
            TimeProvider.System);
        return new MeshHeartbeatBackgroundService(
            monitor,
            new StubIdentity(),
            new StubIdGenerator(),
            Options.Create(new MeshFailureDetectorOptions
            {
                HeartbeatIntervalMilliseconds = 1,
                SuspectAfterMilliseconds = 2,
                UnreachableAfterMilliseconds = 3,
            }),
            TimeProvider.System,
            NullLogger<MeshHeartbeatBackgroundService>.Instance);
    }

    private sealed class SequencedProbe(
        int targetCalls = 1,
        bool throwOnFirstCall = false,
        bool returnFailure = false) : IMeshPeerProbe
    {
        private int _activeCalls;
        private int _callCount;
        private int _maximumConcurrentCalls;

        public TaskCompletionSource TargetReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _callCount);

        public int MaximumConcurrentCalls => Volatile.Read(
            ref _maximumConcurrentCalls);

        public string? LastCorrelationId { get; private set; }

        public Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCorrelationId = correlationId;
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(active);
            var call = Interlocked.Increment(ref _callCount);
            try
            {
                if (call >= targetCalls)
                {
                    TargetReached.TrySetResult();
                }

                if (throwOnFirstCall && call == 1)
                {
                    throw new InvalidOperationException("Unexpected test failure.");
                }

                IReadOnlyCollection<MeshPeerProbeResult> results =
                [
                    returnFailure
                        ? InMemoryMeshPeerHealthRegistryTests.Failure(
                            "node-b",
                            MeshErrorCodes.PeerUnreachable)
                        : InMemoryMeshPeerHealthRegistryTests.Success(
                            "node-b",
                            Guid.Parse("c26b7d86-b2e5-44d3-bd16-d9d48fc8cdd2")),
                ];
                return Task.FromResult(results);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaximum(int value)
        {
            var current = Volatile.Read(ref _maximumConcurrentCalls);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref _maximumConcurrentCalls,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class StubIdentity : IMeshNodeIdentity
    {
        public NodeId NodeId { get; } = new("node-a");

        public Guid InstanceId { get; } =
            Guid.Parse("7778437b-974f-4ed6-88a0-c9ca5e26b629");
    }

    private sealed class StubIdGenerator : IIdGenerator
    {
        private int _value;

        public Guid NewId()
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(Interlocked.Increment(ref _value))
                .CopyTo(bytes, 0);
            return new Guid(bytes);
        }
    }
}
