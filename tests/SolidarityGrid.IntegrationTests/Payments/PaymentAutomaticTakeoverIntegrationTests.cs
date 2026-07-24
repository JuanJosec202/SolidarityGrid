using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Application.Payments.Recovery;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Persistence;
using SolidarityGrid.IntegrationTests.Persistence;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Payments;

public sealed class PaymentAutomaticTakeoverIntegrationTests
{
    [Fact]
    public async Task TwoSurvivorsCompeteAndOneCompletesWithHigherTerm()
    {
        var now = TimeProvider.System.GetUtcNow();
        var paymentId = Guid.NewGuid();
        await using var nodeA = new SqliteTestDatabase();
        await using var nodeB = new SqliteTestDatabase();
        await using var nodeC = new SqliteTestDatabase();
        var databases = new Dictionary<NodeId, SqliteTestDatabase>
        {
            [new NodeId("node-a")] = nodeA,
            [new NodeId("node-b")] = nodeB,
            [new NodeId("node-c")] = nodeC,
        };
        foreach (var database in databases.Values)
        {
            await database.InitializeAsync();
            await SeedProcessingAsync(database, paymentId, now);
        }

        var unreachableOwner = Snapshot(
            new NodeId("node-a"),
            MeshPeerHealthStatus.Unreachable,
            now);
        await using var contextB = nodeB.CreateContext();
        await using var contextC = nodeC.CreateContext();
        var paymentB = await contextB.Payments.SingleAsync();
        var paymentC = await contextC.Payments.SingleAsync();
        Assert.Equal(
            PaymentRecoveryDecision.Eligible,
            PaymentRecoveryPolicy.Evaluate(
                paymentB,
                new NodeId("node-b"),
                unreachableOwner,
                now));
        Assert.Equal(
            PaymentRecoveryDecision.Eligible,
            PaymentRecoveryPolicy.Evaluate(
                paymentC,
                new NodeId("node-c"),
                unreachableOwner,
                now));

        var observer = new RecordingObserver();
        var orchestratorB = CreateOrchestrator(
            new NodeId("node-b"),
            contextB,
            databases,
            observer);
        var orchestratorC = CreateOrchestrator(
            new NodeId("node-c"),
            contextC,
            databases,
            observer);

        var results = await Task.WhenAll(
            orchestratorB.ProcessAsync(
                paymentB,
                "integrated-takeover-b",
                CancellationToken.None),
            orchestratorC.ProcessAsync(
                paymentC,
                "integrated-takeover-c",
                CancellationToken.None));

        var completed = Assert.Single(results, result =>
            result.Status == PaymentProcessingStatus.AcquiredAndCompleted);
        Assert.Equal(PaymentProcessingMode.TakeoverProcessing, completed.Mode);
        Assert.Equal(2, completed.Term);
        Assert.Equal(1, observer.PaymentCompletedCount);

        contextB.ChangeTracker.Clear();
        contextC.ChangeTracker.Clear();
        var survivorB = await contextB.Payments.SingleAsync();
        var survivorC = await contextC.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Completed, survivorB.Status);
        Assert.Equal(PaymentStatus.Completed, survivorC.Status);
        Assert.Equal(2, survivorB.Term);
        Assert.Equal(2, survivorC.Term);
        Assert.Equal(2, survivorB.Attempt);
        Assert.Equal(2, survivorC.Attempt);
        Assert.Equal(survivorB.OwnerNodeId, survivorC.OwnerNodeId);
        Assert.NotEqual(new NodeId("node-a"), survivorB.OwnerNodeId);

        var oldRenewal = await ApplyOldRenewalAsync(
            databases[survivorB.OwnerNodeId!],
            paymentId,
            now);
        var oldCompletion = await ApplyOldCompletionAsync(
            databases[survivorB.OwnerNodeId!],
            paymentId,
            now);
        Assert.False(oldRenewal.Applied);
        Assert.False(oldCompletion.Applied);

        await using var verification =
            databases[survivorB.OwnerNodeId!].CreateContext();
        var fenced = await verification.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Completed, fenced.Status);
        Assert.Equal(2, fenced.Term);
        Assert.Equal(2, fenced.Attempt);
        Assert.Equal(survivorB.OwnerNodeId, fenced.OwnerNodeId);
    }

    private static PaymentProcessingOrchestrator CreateOrchestrator(
        NodeId localNodeId,
        SolidarityGridDbContext context,
        IReadOnlyDictionary<NodeId, SqliteTestDatabase> databases,
        IPaymentCoordinationObserver observer)
    {
        var identity = new TestIdentity(localNodeId);
        var options = new PaymentProcessingOptions
        {
            ProcessingDurationMilliseconds = 100,
            LeaseDurationMilliseconds = 250,
            LeaseRenewalIntervalMilliseconds = 25,
            ScanIntervalMilliseconds = 800,
            BatchSize = 10,
        };
        var transport = new LogicalClusterTransport(
            localNodeId,
            databases,
            unavailableNodeId: new NodeId("node-a"));
        var unitOfWork = new SolidarityGridUnitOfWork(context);
        var coordinator = new PaymentOwnershipCoordinator(
            transport,
            unitOfWork,
            TimeProvider.System,
            identity,
            new TestPeerDirectory(localNodeId, databases.Keys),
            options,
            observer);
        return new PaymentProcessingOrchestrator(
            coordinator,
            transport,
            unitOfWork,
            TimeProvider.System,
            identity,
            options,
            observer);
    }

    private static async Task SeedProcessingAsync(
        SqliteTestDatabase database,
        Guid paymentId,
        DateTimeOffset now)
    {
        var createdAt = now.AddSeconds(-10);
        var owner = new NodeId("node-a");
        var payment = Payment.Create(
            paymentId,
            new IdempotencyKey("INTEGRATED-TAKEOVER"),
            new Money(125m, "COP"),
            createdAt);
        payment.MarkReplicated(createdAt.AddSeconds(1));
        payment.Claim(
            owner,
            1,
            now.AddSeconds(-1),
            createdAt.AddSeconds(2));
        payment.StartProcessing(owner, 1, createdAt.AddSeconds(3));
        payment.DequeueDomainEvents();

        await using var context = database.CreateContext();
        context.Add(payment);
        await context.SaveChangesAsync();
    }

    private static async Task<ReceivePaymentCoordinationResult>
        ApplyOldRenewalAsync(
            SqliteTestDatabase database,
            Guid paymentId,
            DateTimeOffset now)
    {
        await using var context = database.CreateContext();
        return await new ReceivePaymentLeaseRenewalUseCase(
            new PaymentRepository(context),
            new SolidarityGridUnitOfWork(context)).ExecuteAsync(
                new PaymentCoordinationCommand(
                    paymentId,
                    new NodeId("node-a"),
                    1,
                    now,
                    now.AddMinutes(1)),
                CancellationToken.None);
    }

    private static async Task<ReceivePaymentCoordinationResult>
        ApplyOldCompletionAsync(
            SqliteTestDatabase database,
            Guid paymentId,
            DateTimeOffset now)
    {
        await using var context = database.CreateContext();
        return await new ReceivePaymentCompletionUseCase(
            new PaymentRepository(context),
            new SolidarityGridUnitOfWork(context)).ExecuteAsync(
                new PaymentCoordinationCommand(
                    paymentId,
                    new NodeId("node-a"),
                    1,
                    now),
                CancellationToken.None);
    }

    private static MeshPeerHealthSnapshot Snapshot(
        NodeId nodeId,
        MeshPeerHealthStatus status,
        DateTimeOffset now) =>
        new(
            nodeId,
            new Uri($"http://{nodeId.Value}:8081"),
            status,
            Guid.NewGuid(),
            now.AddMinutes(-1),
            now,
            null,
            now,
            now,
            10,
            0,
            10,
            0,
            TimeSpan.FromMilliseconds(1),
            "MESH_PEER_UNREACHABLE",
            "Unavailable in integrated test.",
            1);

    private sealed class TestIdentity(NodeId nodeId) : IMeshNodeIdentity
    {
        public NodeId NodeId { get; } = nodeId;

        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    private sealed class TestPeerDirectory : IMeshPeerDirectory
    {
        private readonly IReadOnlyCollection<MeshPeer> _peers;

        public TestPeerDirectory(
            NodeId localNodeId,
            IEnumerable<NodeId> nodeIds)
        {
            _peers = nodeIds
                .Where(nodeId => nodeId != localNodeId)
                .Select(nodeId => new MeshPeer(
                    nodeId,
                    new Uri($"http://{nodeId.Value}:8081")))
                .ToArray();
        }

        public IReadOnlyCollection<MeshPeer> GetPeers() => _peers;

        public bool Contains(NodeId nodeId) =>
            _peers.Any(peer => peer.NodeId == nodeId);
    }

    private sealed class LogicalClusterTransport(
        NodeId localNodeId,
        IReadOnlyDictionary<NodeId, SqliteTestDatabase> databases,
        NodeId unavailableNodeId) : IPaymentCoordinationTransport
    {
        public async Task<IReadOnlyCollection<PaymentClaimPeerResult>>
            TryClaimAsync(
                PaymentClaimRequest request,
                string correlationId,
                CancellationToken cancellationToken)
        {
            var results = new List<PaymentClaimPeerResult>();
            foreach (var peer in PeerNodeIds())
            {
                if (peer == unavailableNodeId)
                {
                    results.Add(new PaymentClaimPeerResult(
                        peer,
                        false,
                        false,
                        1,
                        unavailableNodeId,
                        null,
                        PaymentCoordinationErrorCodes.PeerUnavailable));
                    continue;
                }

                await using var context = databases[peer].CreateContext();
                var result = await new ReceivePaymentClaimUseCase(
                    new PaymentRepository(context),
                    new SolidarityGridUnitOfWork(context)).ExecuteAsync(
                        request,
                        cancellationToken);
                results.Add(new PaymentClaimPeerResult(
                    peer,
                    result.Granted,
                    result.AlreadyApplied,
                    result.CurrentTerm,
                    result.CurrentOwnerNodeId,
                    result.LeaseExpiresAtUtc,
                    result.ErrorCode));
            }

            return results;
        }

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            StartProcessingAsync(
                PaymentCoordinationCommand command,
                string correlationId,
                CancellationToken cancellationToken) =>
            ApplyAsync(
                command,
                static (repository, unitOfWork, value, token) =>
                    new ReceivePaymentProcessingStartedUseCase(
                        repository,
                        unitOfWork).ExecuteAsync(value, token),
                cancellationToken);

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            RenewLeaseAsync(
                PaymentCoordinationCommand command,
                string correlationId,
                CancellationToken cancellationToken) =>
            ApplyAsync(
                command,
                static (repository, unitOfWork, value, token) =>
                    new ReceivePaymentLeaseRenewalUseCase(
                        repository,
                        unitOfWork).ExecuteAsync(value, token),
                cancellationToken);

        public Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            CompleteAsync(
                PaymentCoordinationCommand command,
                string correlationId,
                CancellationToken cancellationToken) =>
            ApplyAsync(
                command,
                static (repository, unitOfWork, value, token) =>
                    new ReceivePaymentCompletionUseCase(
                        repository,
                        unitOfWork).ExecuteAsync(value, token),
                cancellationToken);

        private async Task<IReadOnlyCollection<PaymentCoordinationPeerResult>>
            ApplyAsync(
                PaymentCoordinationCommand command,
                Func<
                    IPaymentRepository,
                    IUnitOfWork,
                    PaymentCoordinationCommand,
                    CancellationToken,
                    Task<ReceivePaymentCoordinationResult>> apply,
                CancellationToken cancellationToken)
        {
            var results = new List<PaymentCoordinationPeerResult>();
            foreach (var peer in PeerNodeIds())
            {
                if (peer == unavailableNodeId)
                {
                    results.Add(new PaymentCoordinationPeerResult(
                        peer,
                        false,
                        false,
                        PaymentCoordinationErrorCodes.PeerUnavailable));
                    continue;
                }

                await using var context = databases[peer].CreateContext();
                var result = await apply(
                    new PaymentRepository(context),
                    new SolidarityGridUnitOfWork(context),
                    command,
                    cancellationToken);
                results.Add(new PaymentCoordinationPeerResult(
                    peer,
                    result.Applied,
                    result.AlreadyApplied,
                    result.ErrorCode));
            }

            return results;
        }

        private IEnumerable<NodeId> PeerNodeIds() =>
            databases.Keys.Where(nodeId => nodeId != localNodeId);
    }

    private sealed class RecordingObserver : IPaymentCoordinationObserver
    {
        private int _paymentCompletedCount;

        public int PaymentCompletedCount =>
            Volatile.Read(ref _paymentCompletedCount);

        public void ClaimStarted(
            Guid paymentId,
            string correlationId,
            long term)
        {
        }

        public void ClaimPeerResult(
            Guid paymentId,
            string correlationId,
            string peerNodeId,
            bool granted,
            long term,
            string? errorCode)
        {
        }

        public void OwnershipAcquired(
            Guid paymentId,
            string correlationId,
            string ownerNodeId,
            long term)
        {
        }

        public void TakeoverStarted(
            Guid paymentId,
            string correlationId,
            string previousOwnerNodeId,
            long previousTerm,
            string newOwnerNodeId,
            long newTerm,
            DateTimeOffset previousLeaseExpiresAtUtc)
        {
        }

        public void TakeoverAcquired(
            Guid paymentId,
            string correlationId,
            string previousOwnerNodeId,
            long previousTerm,
            string newOwnerNodeId,
            long newTerm)
        {
        }

        public void TakeoverRejected(
            Guid paymentId,
            string correlationId,
            string previousOwnerNodeId,
            long previousTerm,
            string newOwnerNodeId,
            long proposedTerm,
            string errorCode)
        {
        }

        public void ProcessingStarted(
            Guid paymentId,
            string correlationId,
            string ownerNodeId,
            long term,
            int attempt,
            int successfulPeers)
        {
        }

        public void LeaseRenewed(
            Guid paymentId,
            string correlationId,
            string ownerNodeId,
            long term,
            int successfulPeers)
        {
        }

        public void ProcessingAborted(
            Guid paymentId,
            string correlationId,
            string ownerNodeId,
            long term,
            string errorCode)
        {
        }

        public void CompletionQuorumReached(
            Guid paymentId,
            string correlationId,
            string ownerNodeId,
            long term,
            int successfulPeers)
        {
        }

        public void PaymentCompleted(
            Guid paymentId,
            string correlationId,
            string ownerNodeId,
            long term,
            int attempt) =>
            Interlocked.Increment(ref _paymentCompletedCount);

        public void RecoveredPaymentProcessingStarted(
            Guid paymentId,
            string correlationId,
            string previousOwnerNodeId,
            string newOwnerNodeId,
            long previousTerm,
            long newTerm,
            int attempt)
        {
        }

        public void RecoveredPaymentCompleted(
            Guid paymentId,
            string correlationId,
            string previousOwnerNodeId,
            string newOwnerNodeId,
            long previousTerm,
            long newTerm,
            int attempt)
        {
        }
    }
}
