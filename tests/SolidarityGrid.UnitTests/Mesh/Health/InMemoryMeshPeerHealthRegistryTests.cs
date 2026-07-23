using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh.Health;

public sealed class InMemoryMeshPeerHealthRegistryTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirstInstance =
        Guid.Parse("3f94553f-f562-4588-bc54-f79a21bacb26");

    [Fact]
    public void InitializesExactlyConfiguredPeersAsUnknown()
    {
        var registry = CreateRegistry();

        var snapshots = registry.GetSnapshots();

        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(MeshPeerHealthStatus.Unknown, snapshot.Status);
            Assert.Null(snapshot.InstanceId);
            Assert.Equal(StartedAt, snapshot.MonitoringStartedAtUtc);
            Assert.Equal(0, snapshot.ObservationVersion);
            Assert.Equal(0, snapshot.TotalSuccessfulProbes);
            Assert.Equal(0, snapshot.TotalFailedProbes);
            Assert.Null(snapshot.LastErrorCode);
        });
    }

    [Fact]
    public void FirstSuccessChangesUnknownToAliveWithoutRestart()
    {
        var transition = CreateRegistry().RecordSuccess(
            Success("node-b", FirstInstance),
            StartedAt.AddSeconds(1));

        Assert.True(transition.StatusChanged);
        Assert.Equal(MeshPeerHealthStatus.Alive, transition.Current.Status);
        Assert.Equal(FirstInstance, transition.Current.InstanceId);
        Assert.Equal(1, transition.Current.TotalSuccessfulProbes);
        Assert.Equal(1, transition.Current.ObservationVersion);
        Assert.False(transition.RestartDetected);
        Assert.Equal(0, transition.Current.RestartCount);
    }

    [Fact]
    public void RepeatedSuccessKeepsAliveAndIncrementsCounters()
    {
        var registry = CreateRegistry();
        registry.RecordSuccess(Success("node-b", FirstInstance), StartedAt.AddSeconds(1));

        var transition = registry.RecordSuccess(
            Success("node-b", FirstInstance),
            StartedAt.AddSeconds(2));

        Assert.False(transition.StatusChanged);
        Assert.False(transition.RestartDetected);
        Assert.Equal(2, transition.Current.TotalSuccessfulProbes);
        Assert.Equal(2, transition.Current.ObservationVersion);
    }

    [Fact]
    public void IsolatedFailurePreservesAliveAndLastSuccess()
    {
        var registry = CreateRegistry();
        var successfulAt = StartedAt.AddSeconds(1);
        registry.RecordSuccess(Success("node-b", FirstInstance), successfulAt);

        var transition = registry.RecordFailure(
            Failure("node-b", MeshErrorCodes.DeadlineExceeded),
            successfulAt.AddSeconds(1));

        Assert.Equal(MeshPeerHealthStatus.Alive, transition.Current.Status);
        Assert.Equal(successfulAt, transition.Current.LastSuccessfulProbeAtUtc);
        Assert.Equal(1, transition.Current.ConsecutiveFailures);
        Assert.Equal(1, transition.Current.TotalFailedProbes);
        Assert.Equal(MeshErrorCodes.DeadlineExceeded, transition.Current.LastErrorCode);
    }

    [Fact]
    public void TransientFailuresProgressByElapsedTime()
    {
        var registry = CreateRegistry();
        registry.RecordSuccess(Success("node-b", FirstInstance), StartedAt.AddSeconds(1));

        var suspected = registry.RecordFailure(
            Failure("node-b", MeshErrorCodes.PeerUnreachable),
            StartedAt.AddSeconds(4));
        var unreachable = registry.RecordFailure(
            Failure("node-b", MeshErrorCodes.PeerUnreachable),
            StartedAt.AddSeconds(6));

        Assert.Equal(MeshPeerHealthStatus.Suspected, suspected.Current.Status);
        Assert.Equal(MeshPeerHealthStatus.Unreachable, unreachable.Current.Status);
        Assert.Equal(2, unreachable.Current.ConsecutiveFailures);
        Assert.Equal(3, unreachable.Current.ObservationVersion);
    }

    [Theory]
    [InlineData(MeshErrorCodes.ProtocolMismatch)]
    [InlineData(MeshErrorCodes.IdentityMismatch)]
    [InlineData(MeshErrorCodes.PermissionDenied)]
    public void TrustFailuresAreImmediatelyUnreachable(string errorCode)
    {
        var transition = CreateRegistry().RecordFailure(
            Failure("node-b", errorCode),
            StartedAt.AddMilliseconds(100));

        Assert.Equal(MeshPeerHealthStatus.Unreachable, transition.Current.Status);
        Assert.True(transition.StatusChanged);
    }

    [Fact]
    public void CancelledObservationDoesNotModifySnapshot()
    {
        var registry = CreateRegistry();
        var before = registry.GetSnapshot(new NodeId("node-b"));

        var transition = registry.RecordFailure(
            Failure("node-b", MeshErrorCodes.Cancelled),
            StartedAt.AddSeconds(10));

        Assert.Same(before, transition.Current);
        Assert.Equal(0, transition.Current.ObservationVersion);
        Assert.Equal(0, transition.Current.TotalFailedProbes);
    }

    [Theory]
    [InlineData(4, MeshPeerHealthStatus.Suspected)]
    [InlineData(6, MeshPeerHealthStatus.Unreachable)]
    public void SuccessRecoversSuspectedOrUnreachable(
        int failureSecond,
        MeshPeerHealthStatus expectedPreviousStatus)
    {
        var registry = CreateRegistry();
        registry.RecordSuccess(Success("node-b", FirstInstance), StartedAt.AddSeconds(1));
        registry.RecordFailure(
            Failure("node-b", MeshErrorCodes.PeerUnreachable),
            StartedAt.AddSeconds(failureSecond));

        var transition = registry.RecordSuccess(
            Success("node-b", FirstInstance),
            StartedAt.AddSeconds(failureSecond + 1));

        Assert.Equal(expectedPreviousStatus, transition.Previous.Status);
        Assert.Equal(MeshPeerHealthStatus.Alive, transition.Current.Status);
        Assert.True(transition.Recovered);
        Assert.Equal(0, transition.Current.ConsecutiveFailures);
        Assert.Null(transition.Current.LastErrorCode);
    }

    [Fact]
    public void ChangedInstanceDetectsAndCountsRestarts()
    {
        var registry = CreateRegistry();
        var second = Guid.Parse("1607a15a-f55c-4400-ab62-88c7d84e4c03");
        var third = Guid.Parse("e5da5173-b7e9-40dd-b1f7-fdc8fd0ea8db");
        registry.RecordSuccess(Success("node-b", FirstInstance), StartedAt.AddSeconds(1));

        var firstRestart = registry.RecordSuccess(
            Success("node-b", second),
            StartedAt.AddSeconds(2));
        var noRestart = registry.RecordSuccess(
            Success("node-b", second),
            StartedAt.AddSeconds(3));
        var secondRestart = registry.RecordSuccess(
            Success("node-b", third),
            StartedAt.AddSeconds(4));

        Assert.True(firstRestart.RestartDetected);
        Assert.Equal(1, firstRestart.Current.RestartCount);
        Assert.False(noRestart.RestartDetected);
        Assert.True(secondRestart.RestartDetected);
        Assert.Equal(2, secondRestart.Current.RestartCount);
    }

    [Fact]
    public void PreviouslyReadSnapshotsRemainImmutable()
    {
        var registry = CreateRegistry();
        var before = registry.GetSnapshot(new NodeId("node-b"))!;

        registry.RecordSuccess(Success("node-b", FirstInstance), StartedAt.AddSeconds(1));
        var after = registry.GetSnapshot(new NodeId("node-b"))!;

        Assert.Equal(MeshPeerHealthStatus.Unknown, before.Status);
        Assert.Equal(0, before.ObservationVersion);
        Assert.Equal(MeshPeerHealthStatus.Alive, after.Status);
    }

    [Fact]
    public void SnapshotCollectionIsReadOnly()
    {
        var snapshots = Assert.IsAssignableFrom<
            ICollection<MeshPeerHealthSnapshot>>(CreateRegistry().GetSnapshots());

        Assert.True(snapshots.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => snapshots.Clear());
    }

    [Fact]
    public void ConcurrentReadsAndUpdatesReturnCompleteSnapshots()
    {
        var registry = CreateRegistry();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Parallel.For(0, 200, index =>
        {
            try
            {
                if (index % 3 == 0)
                {
                    registry.RecordSuccess(
                        Success("node-b", FirstInstance),
                        StartedAt.AddMilliseconds(index + 1));
                }
                else
                {
                    var snapshots = registry.GetSnapshots();
                    Assert.Equal(2, snapshots.Count);
                    Assert.All(snapshots, snapshot =>
                        Assert.NotNull(snapshot.PeerNodeId));
                }
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownPeerIsRejected()
    {
        var exception = Assert.Throws<KeyNotFoundException>(
            () => CreateRegistry().RecordSuccess(
                Success("node-z", FirstInstance),
                StartedAt));

        Assert.Contains("node-z", exception.Message, StringComparison.Ordinal);
    }

    private static InMemoryMeshPeerHealthRegistry CreateRegistry() =>
        new(
            [
                new MeshPeer(new NodeId("node-b"), new Uri("http://node-b:8081")),
                new MeshPeer(new NodeId("node-c"), new Uri("http://node-c:8081")),
            ],
            StartedAt,
            new MeshFailureDetectionThresholds(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5)));

    internal static MeshPeerProbeResult Success(string nodeId, Guid instanceId) =>
        new(
            new NodeId(nodeId),
            new Uri($"http://{nodeId}:8081"),
            true,
            new NodeId(nodeId),
            instanceId,
            1,
            TimeSpan.FromMilliseconds(10),
            null,
            null);

    internal static MeshPeerProbeResult Failure(string nodeId, string errorCode) =>
        new(
            new NodeId(nodeId),
            new Uri($"http://{nodeId}:8081"),
            false,
            null,
            null,
            null,
            TimeSpan.FromMilliseconds(50),
            errorCode,
            "Safe mesh failure.");
}
