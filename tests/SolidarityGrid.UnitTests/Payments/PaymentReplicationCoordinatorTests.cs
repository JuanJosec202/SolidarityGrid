using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentReplicationCoordinatorTests
{
    private static readonly DateTimeOffset ReplicatedAt =
        new(2026, 7, 25, 11, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 1)]
    public async Task AtLeastOneStoredPeerReachesQuorum(
        bool peerOneStored,
        bool peerTwoStored,
        int expectedSuccesses)
    {
        var fixture = CreateFixture(peerOneStored, peerTwoStored);
        var payment = CreatePayment();

        var outcome = await fixture.Coordinator.EnsureReplicatedAsync(
            payment,
            "replication-correlation",
            CancellationToken.None);

        Assert.True(outcome.QuorumReached);
        Assert.Equal(PaymentStatus.Replicated, payment.Status);
        Assert.Equal(2, payment.Version);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(expectedSuccesses, outcome.ReplicaResults.Count(x => x.Stored));
        Assert.Equal(ReplicatedAt, fixture.Transport.Replica?.ReplicatedAtUtc);
        Assert.Equal(ReplicatedAt, payment.UpdatedAtUtc);
    }

    [Fact]
    public async Task NoRemoteAcknowledgementLeavesPaymentReceivedWithoutSave()
    {
        var fixture = CreateFixture(false, false);
        var payment = CreatePayment();

        var outcome = await fixture.Coordinator.EnsureReplicatedAsync(
            payment,
            "no-quorum",
            CancellationToken.None);

        Assert.False(outcome.QuorumReached);
        Assert.Equal(PaymentStatus.Received, payment.Status);
        Assert.Equal(1, payment.Version);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task AlreadyReplicatedIsNoOp()
    {
        var fixture = CreateFixture(true, true);
        var payment = CreatePayment();
        payment.MarkReplicated(ReplicatedAt.AddSeconds(-1));
        var version = payment.Version;

        var outcome = await fixture.Coordinator.EnsureReplicatedAsync(
            payment,
            "already",
            CancellationToken.None);

        Assert.True(outcome.QuorumReached);
        Assert.True(outcome.WasAlreadyReplicated);
        Assert.Equal(0, fixture.Transport.Calls);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(version, payment.Version);
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutMutation()
    {
        var fixture = CreateFixture(true, true);
        var payment = CreatePayment();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.EnsureReplicatedAsync(
                payment,
                "cancelled",
                cancellation.Token));

        Assert.Equal(PaymentStatus.Received, payment.Status);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    private static Fixture CreateFixture(bool peerOneStored, bool peerTwoStored)
    {
        var transport = new FakeTransport(
        [
            Result("peer-one", peerOneStored),
            Result("peer-two", peerTwoStored),
        ]);
        var unitOfWork = new FakeUnitOfWork();
        return new Fixture(
            new PaymentReplicationCoordinator(
                transport,
                unitOfWork,
                new FixedTimeProvider(ReplicatedAt),
                new NullObserver()),
            transport,
            unitOfWork);
    }

    private static Payment CreatePayment() =>
        Payment.Create(
            Guid.Parse("bc42fd1d-ddd1-4e81-bfc4-9fdb34099f42"),
            new IdempotencyKey("COORDINATOR-1"),
            new Money(10m, "COP"),
            ReplicatedAt.AddMinutes(-1));

    private static PaymentReplicaResult Result(string nodeId, bool stored) =>
        new(
            new NodeId(nodeId),
            stored,
            false,
            stored ? null : PaymentReplicaErrorCodes.PeerUnavailable,
            stored ? null : "Unavailable.");

    private sealed record Fixture(
        PaymentReplicationCoordinator Coordinator,
        FakeTransport Transport,
        FakeUnitOfWork UnitOfWork);

    private sealed class FakeTransport(
        IReadOnlyCollection<PaymentReplicaResult> results)
        : IPaymentReplicaTransport
    {
        public int Calls { get; private set; }

        public PaymentReplica? Replica { get; private set; }

        public Task<IReadOnlyCollection<PaymentReplicaResult>> ReplicateAsync(
            PaymentReplica replica,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Replica = replica;
            return Task.FromResult(results);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NullObserver : IPaymentReplicationObserver
    {
        public void ReplicationStarted(Guid paymentId, string correlationId)
        {
        }

        public void ReplicationCompleted(
            Guid paymentId,
            string correlationId,
            int successfulReplicas,
            bool quorumReached,
            TimeSpan duration)
        {
        }
    }
}
