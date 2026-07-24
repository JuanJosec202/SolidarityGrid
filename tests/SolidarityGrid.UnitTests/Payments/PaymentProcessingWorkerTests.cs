using Microsoft.Extensions.DependencyInjection;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentProcessingWorkerTests
{
    private static readonly DateTimeOffset CycleTime =
        PaymentTestData.CreatedAt.AddSeconds(10);

    [Fact]
    public async Task CycleProcessesEveryReplicatedPaymentSequentially()
    {
        var payments = new[]
        {
            CreateReplicated(Guid.NewGuid(), "WORKER-1"),
            CreateReplicated(Guid.NewGuid(), "WORKER-2"),
        };
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(replicated: payments, processor: processor);

        var count = await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(payments.Select(payment => payment.Id), processor.PaymentIds);
        Assert.Equal(1, processor.MaximumConcurrency);
    }

    [Fact]
    public async Task FailureOfOnePaymentDoesNotPreventTheNextPayment()
    {
        var payments = new[]
        {
            CreateReplicated(Guid.NewGuid(), "WORKER-FAIL"),
            CreateReplicated(Guid.NewGuid(), "WORKER-NEXT"),
        };
        var processor = new RecordingProcessor(throwOnFirst: true);
        var cycle = CreateCycle(replicated: payments, processor: processor);

        var count = await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, processor.Calls);
        Assert.Contains(payments[1].Id, processor.PaymentIds);
    }

    [Fact]
    public async Task CycleRespectsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cycle = CreateCycle(
            replicated: [CreateReplicated(Guid.NewGuid(), "WORKER-CANCEL")]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cycle.ExecuteAsync(cancellation.Token));
    }

    [Fact]
    public async Task BackgroundServiceCreatesOneScopePerCycle()
    {
        var scopeFactory = new CountingScopeFactory(() => CreateCycle());
        var worker = new PaymentProcessingBackgroundService(
            scopeFactory,
            ValidOptions(),
            TimeProvider.System);

        await worker.ExecuteCycleAsync(CancellationToken.None);
        await worker.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(2, scopeFactory.CreatedScopes);
        Assert.Equal(2, scopeFactory.DisposedScopes);
        Assert.Equal(2, scopeFactory.CreatedCycles);
    }

    [Fact]
    public async Task RepositoryQueriesReceiveConfiguredBatchSize()
    {
        var repository = new FakeRepository([], []);
        var options = new PaymentProcessingOptions
        {
            ProcessingDurationMilliseconds = 8_000,
            LeaseDurationMilliseconds = 4_000,
            LeaseRenewalIntervalMilliseconds = 1_000,
            ScanIntervalMilliseconds = 500,
            BatchSize = 37,
        };
        var cycle = CreateCycle(repository: repository, options: options);

        await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(37, repository.ReplicatedLimit);
        Assert.Equal(37, repository.RecoverableLimit);
    }

    [Fact]
    public async Task UnreachableRemoteOwnerWithExpiredLeaseIsRecovered()
    {
        var payment = CreateProcessing(
            new NodeId("node-b"),
            CycleTime.AddSeconds(-1));
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(
            recoverable: [payment],
            processor: processor,
            ownerHealth: MeshPeerHealthStatus.Unreachable);

        var count = await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal([payment.Id], processor.PaymentIds);
    }

    [Theory]
    [InlineData(MeshPeerHealthStatus.Alive)]
    [InlineData(MeshPeerHealthStatus.Suspected)]
    [InlineData(MeshPeerHealthStatus.Unknown)]
    public async Task OwnerNotUnreachableIsNotRecovered(
        MeshPeerHealthStatus status)
    {
        var payment = CreateProcessing(
            new NodeId("node-b"),
            CycleTime.AddSeconds(-1));
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(
            recoverable: [payment],
            processor: processor,
            ownerHealth: status);

        var count = await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(processor.PaymentIds);
    }

    [Fact]
    public async Task ActiveLeaseIsNotRecovered()
    {
        var payment = CreateProcessing(
            new NodeId("node-b"),
            CycleTime.AddSeconds(1));
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(
            recoverable: [payment],
            processor: processor,
            ownerHealth: MeshPeerHealthStatus.Unreachable);

        await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Empty(processor.PaymentIds);
    }

    [Fact]
    public async Task LocalOwnerIsNotRecovered()
    {
        var payment = CreateProcessing(
            new NodeId("node-a"),
            CycleTime.AddSeconds(-1));
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(
            recoverable: [payment],
            processor: processor,
            ownerHealth: MeshPeerHealthStatus.Unreachable);

        await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Empty(processor.PaymentIds);
    }

    [Fact]
    public async Task SamePaymentIsNotProcessedTwiceInOneCycle()
    {
        var payment = CreateProcessing(
            new NodeId("node-b"),
            CycleTime.AddSeconds(-1));
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(
            replicated: [payment],
            recoverable: [payment],
            processor: processor,
            ownerHealth: MeshPeerHealthStatus.Unreachable);

        var count = await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(1, processor.Calls);
    }

    private static PaymentProcessingCycle CreateCycle(
        IReadOnlyCollection<Payment>? replicated = null,
        IReadOnlyCollection<Payment>? recoverable = null,
        IPaymentProcessor? processor = null,
        FakeRepository? repository = null,
        PaymentProcessingOptions? options = null,
        MeshPeerHealthStatus ownerHealth = MeshPeerHealthStatus.Unknown)
    {
        var actualRepository = repository ??
            new FakeRepository(replicated ?? [], recoverable ?? []);
        return new PaymentProcessingCycle(
            actualRepository,
            processor ?? new RecordingProcessor(),
            new FakeIdGenerator(),
            new FakeIdentity(),
            new FakeHealthRegistry(ownerHealth),
            new FrozenTimeProvider(CycleTime),
            options ?? ValidOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                PaymentProcessingCycle>.Instance);
    }

    private static PaymentProcessingOptions ValidOptions() =>
        new()
        {
            ProcessingDurationMilliseconds = 8_000,
            LeaseDurationMilliseconds = 4_000,
            LeaseRenewalIntervalMilliseconds = 1_000,
            ScanIntervalMilliseconds = 500,
            BatchSize = 10,
        };

    private static Payment CreateReplicated(Guid id, string key)
    {
        var payment = Payment.Create(
            id,
            new IdempotencyKey(key),
            new Money(10m, "USD"),
            PaymentTestData.CreatedAt);
        payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(1));
        return payment;
    }

    private static Payment CreateProcessing(
        NodeId ownerNodeId,
        DateTimeOffset leaseExpiresAtUtc)
    {
        var payment = CreateReplicated(
            Guid.NewGuid(),
            $"RECOVERY-{Guid.NewGuid():N}");
        payment.Claim(
            ownerNodeId,
            1,
            leaseExpiresAtUtc,
            PaymentTestData.CreatedAt.AddSeconds(2));
        payment.StartProcessing(
            ownerNodeId,
            1,
            PaymentTestData.CreatedAt.AddSeconds(3));
        return payment;
    }

    private sealed class FakeRepository(
        IReadOnlyCollection<Payment> replicated,
        IReadOnlyCollection<Payment> recoverable) : IPaymentRepository
    {
        public int ReplicatedLimit { get; private set; }

        public int RecoverableLimit { get; private set; }

        public Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplicatedLimit = limit;
            return Task.FromResult(replicated);
        }

        public Task<IReadOnlyCollection<Payment>> GetRecoverablePaymentsAsync(
            DateTimeOffset utcNow,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoverableLimit = limit;
            return Task.FromResult(recoverable);
        }

        public Task<Payment?> GetByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Payment?> GetByIdempotencyKeyAsync(
            IdempotencyKey idempotencyKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask AddAsync(
            Payment payment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProcessor(bool throwOnFirst = false)
        : IPaymentProcessor
    {
        private int _concurrency;

        public int Calls { get; private set; }

        public int MaximumConcurrency { get; private set; }

        public List<Guid> PaymentIds { get; } = [];

        public Task<PaymentProcessingResult> ProcessAsync(
            Payment payment,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            _concurrency++;
            MaximumConcurrency = Math.Max(MaximumConcurrency, _concurrency);
            try
            {
                if (throwOnFirst && Calls == 1)
                {
                    throw new InvalidOperationException("Expected test failure.");
                }

                PaymentIds.Add(payment.Id);
                return Task.FromResult(PaymentProcessingResult.From(
                    PaymentProcessingStatus.NotAcquired));
            }
            finally
            {
                _concurrency--;
            }
        }
    }

    private sealed class FakeIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FakeIdentity : IMeshNodeIdentity
    {
        public NodeId NodeId { get; } = new("node-a");

        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    private sealed class FakeHealthRegistry(MeshPeerHealthStatus status)
        : IMeshPeerHealthRegistry
    {
        private readonly MeshPeerHealthSnapshot _snapshot = new(
            new NodeId("node-b"),
            new Uri("http://node-b:8081"),
            status,
            Guid.NewGuid(),
            CycleTime.AddMinutes(-1),
            CycleTime,
            status == MeshPeerHealthStatus.Alive ? CycleTime : null,
            status == MeshPeerHealthStatus.Alive ? null : CycleTime,
            CycleTime,
            status == MeshPeerHealthStatus.Alive ? 0 : 10,
            status == MeshPeerHealthStatus.Alive ? 1 : 0,
            status == MeshPeerHealthStatus.Alive ? 0 : 10,
            0,
            TimeSpan.FromMilliseconds(1),
            null,
            null,
            1);

        public IReadOnlyCollection<MeshPeerHealthSnapshot> GetSnapshots() =>
            [_snapshot];

        public MeshPeerHealthSnapshot? GetSnapshot(NodeId peerNodeId) =>
            peerNodeId == _snapshot.PeerNodeId ? _snapshot : null;

        public MeshPeerHealthTransition RecordSuccess(
            MeshPeerProbeResult result,
            DateTimeOffset observedAtUtc) =>
            throw new NotSupportedException();

        public MeshPeerHealthTransition RecordFailure(
            MeshPeerProbeResult result,
            DateTimeOffset observedAtUtc) =>
            throw new NotSupportedException();
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CountingScopeFactory(
        Func<PaymentProcessingCycle> cycleFactory) : IServiceScopeFactory
    {
        public int CreatedScopes { get; private set; }

        public int DisposedScopes { get; private set; }

        public int CreatedCycles { get; private set; }

        public IServiceScope CreateScope()
        {
            CreatedScopes++;
            CreatedCycles++;
            return new FakeScope(
                cycleFactory(),
                () => DisposedScopes++);
        }
    }

    private sealed class FakeScope(
        PaymentProcessingCycle cycle,
        Action onDispose) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } =
            new FakeServiceProvider(cycle);

        public void Dispose() => onDispose();
    }

    private sealed class FakeServiceProvider(PaymentProcessingCycle cycle)
        : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(PaymentProcessingCycle) ? cycle : null;
    }
}
