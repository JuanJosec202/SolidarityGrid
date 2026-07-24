using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentOwnershipCoordinatorTests
{
    [Fact]
    public async Task OnePeerGrantAcquiresOwnership()
    {
        var fixture = CreateFixture(
            Results(Granted("node-b"), Unavailable("node-c")));
        var payment = PaymentTestData.CreateReplicated();

        var result = await fixture.Coordinator.TryAcquireAsync(
            payment,
            "claim-one",
            CancellationToken.None);

        Assert.Equal(PaymentOwnershipStatus.Acquired, result.Status);
        Assert.Equal(PaymentTestData.NodeA, payment.OwnerNodeId);
        Assert.Equal(1, payment.Term);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task TwoPeerGrantsStillApplyOneLocalClaim()
    {
        var fixture = CreateFixture(
            Results(Granted("node-b"), Granted("node-c")));
        var payment = PaymentTestData.CreateReplicated();

        var result = await fixture.Coordinator.TryAcquireAsync(
            payment,
            "claim-two",
            CancellationToken.None);

        Assert.Equal(PaymentOwnershipStatus.Acquired, result.Status);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task NoAvailablePeerLeavesLocalPaymentUnchanged()
    {
        var fixture = CreateFixture(
            Results(Unavailable("node-b"), Unavailable("node-c")));
        var payment = PaymentTestData.CreateReplicated();

        var result = await fixture.Coordinator.TryAcquireAsync(
            payment,
            "no-quorum",
            CancellationToken.None);

        Assert.Equal(PaymentOwnershipStatus.QuorumUnavailable, result.Status);
        Assert.Equal(PaymentStatus.Replicated, payment.Status);
        Assert.Null(payment.OwnerNodeId);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ActiveRemoteLeaseStopsWithoutRetry()
    {
        var transport = new QueueTransport(
        [
            Results(
                Rejected(
                    "node-b",
                    PaymentCoordinationErrorCodes.ClaimLeaseActive,
                    currentTerm: 3),
                Unavailable("node-c")),
        ]);
        var fixture = CreateFixture(transport);

        var result = await fixture.Coordinator.TryAcquireAsync(
            PaymentTestData.CreateReplicated(),
            "active-lease",
            CancellationToken.None);

        Assert.Equal(PaymentOwnershipStatus.NotAcquired, result.Status);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(1, transport.ClaimCalls);
    }

    [Fact]
    public async Task StaleTermUsesOneHigherSecondRound()
    {
        var transport = new QueueTransport(
        [
            Results(
                Rejected(
                    "node-b",
                    PaymentCoordinationErrorCodes.ClaimTermStale,
                    currentTerm: 5),
                Rejected(
                    "node-c",
                    PaymentCoordinationErrorCodes.ClaimTermStale,
                    currentTerm: 4)),
            Results(Granted("node-b"), Unavailable("node-c")),
        ]);
        var fixture = CreateFixture(transport);
        var payment = PaymentTestData.CreateReplicated();

        var result = await fixture.Coordinator.TryAcquireAsync(
            payment,
            "stale-term",
            CancellationToken.None);

        Assert.Equal(PaymentOwnershipStatus.Acquired, result.Status);
        Assert.Equal(6, payment.Term);
        Assert.Equal(2, result.Rounds);
        Assert.Equal([1L, 6L], transport.ProposedTerms);
    }

    [Fact]
    public async Task StaleTermNeverExceedsTwoRounds()
    {
        var stale = Results(
            Rejected(
                "node-b",
                PaymentCoordinationErrorCodes.ClaimTermStale,
                currentTerm: 7),
            Rejected(
                "node-c",
                PaymentCoordinationErrorCodes.ClaimTermStale,
                currentTerm: 7));
        var transport = new QueueTransport([stale, stale]);
        var fixture = CreateFixture(transport);

        var result = await fixture.Coordinator.TryAcquireAsync(
            PaymentTestData.CreateReplicated(),
            "two-rounds",
            CancellationToken.None);

        Assert.Equal(PaymentOwnershipStatus.NotAcquired, result.Status);
        Assert.Equal(2, result.Rounds);
        Assert.Equal(2, transport.ClaimCalls);
    }

    [Fact]
    public async Task ThreeConcurrentCandidatesProduceExactlyOneOwner()
    {
        var gate = new ExclusiveClaimTransport();
        var candidates = new[]
        {
            CreateFixture(gate, "node-a"),
            CreateFixture(gate, "node-b"),
            CreateFixture(gate, "node-c"),
        };
        var payments = candidates.Select(_ => PaymentTestData.CreateReplicated()).ToArray();

        var results = await Task.WhenAll(candidates.Select((fixture, index) =>
            fixture.Coordinator.TryAcquireAsync(
                payments[index],
                $"candidate-{index}",
                CancellationToken.None)));

        Assert.Single(results, result =>
            result.Status == PaymentOwnershipStatus.Acquired);
        Assert.Single(payments, payment =>
            payment.Status == PaymentStatus.Claimed);
        var owners = payments
            .Where(payment => payment.OwnerNodeId is not null)
            .Select(payment => payment.OwnerNodeId)
            .Distinct()
            .ToArray();
        Assert.Single(owners);
    }

    private static Fixture CreateFixture(
        IReadOnlyCollection<PaymentClaimPeerResult> results) =>
        CreateFixture(new QueueTransport([results]));

    private static Fixture CreateFixture(
        IPaymentCoordinationTransport transport,
        string nodeId = "node-a")
    {
        var unitOfWork = new FakeUnitOfWork();
        var identity = new FakeIdentity(nodeId);
        var coordinator = new PaymentOwnershipCoordinator(
            transport,
            unitOfWork,
            new FrozenTimeProvider(PaymentTestData.CreatedAt.AddSeconds(10)),
            identity,
            new EmptyDirectory(),
            new PaymentProcessingOptions(),
            new NoOpPaymentCoordinationObserver());
        return new Fixture(coordinator, unitOfWork);
    }

    private static PaymentClaimPeerResult[] Results(
        params PaymentClaimPeerResult[] results) =>
        results;

    private static PaymentClaimPeerResult Granted(string peer) =>
        new(
            new NodeId(peer),
            true,
            false,
            1,
            PaymentTestData.NodeA,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentCoordinationErrorCodes.ClaimGranted);

    private static PaymentClaimPeerResult Unavailable(string peer) =>
        Rejected(
            peer,
            PaymentCoordinationErrorCodes.PeerUnavailable,
            currentTerm: 0);

    private static PaymentClaimPeerResult Rejected(
        string peer,
        string errorCode,
        long currentTerm) =>
        new(
            new NodeId(peer),
            false,
            false,
            currentTerm,
            null,
            null,
            errorCode);

    private sealed record Fixture(
        PaymentOwnershipCoordinator Coordinator,
        FakeUnitOfWork UnitOfWork);

    private sealed class FakeIdentity(string nodeId) : IMeshNodeIdentity
    {
        public NodeId NodeId { get; } = new(nodeId);

        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    private sealed class EmptyDirectory : IMeshPeerDirectory
    {
        public IReadOnlyCollection<MeshPeer> GetPeers() => [];

        public bool Contains(NodeId nodeId) => false;
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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

    private sealed class QueueTransport(
        IEnumerable<IReadOnlyCollection<PaymentClaimPeerResult>> responses)
        : IPaymentCoordinationTransport
    {
        private readonly Queue<IReadOnlyCollection<PaymentClaimPeerResult>>
            _responses = new(responses);

        public int ClaimCalls { get; private set; }

        public List<long> ProposedTerms { get; } = [];

        public Task<IReadOnlyCollection<PaymentClaimPeerResult>> TryClaimAsync(
            PaymentClaimRequest request,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimCalls++;
            ProposedTerms.Add(request.ProposedTerm);
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            StartProcessingAsync(
                PaymentCoordinationCommand command,
                string correlationId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> RenewLeaseAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> CompleteAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ExclusiveClaimTransport : IPaymentCoordinationTransport
    {
        private readonly object _gate = new();
        private NodeId? _winner;

        public Task<IReadOnlyCollection<PaymentClaimPeerResult>> TryClaimAsync(
            PaymentClaimRequest request,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _winner ??= request.CandidateNodeId;
                var granted = _winner == request.CandidateNodeId;
                IReadOnlyCollection<PaymentClaimPeerResult> results =
                [
                    new(
                        new NodeId("shared-peer"),
                        granted,
                        false,
                        request.ProposedTerm,
                        _winner,
                        request.LeaseExpiresAtUtc,
                        granted
                            ? PaymentCoordinationErrorCodes.ClaimGranted
                            : PaymentCoordinationErrorCodes.ClaimLeaseActive),
                ];
                return Task.FromResult(results);
            }
        }

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            StartProcessingAsync(
                PaymentCoordinationCommand command,
                string correlationId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> RenewLeaseAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>> CompleteAsync(
            PaymentCoordinationCommand command,
            string correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
