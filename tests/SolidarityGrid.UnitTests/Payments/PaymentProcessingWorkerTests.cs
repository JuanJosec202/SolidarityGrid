using Microsoft.Extensions.DependencyInjection;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentProcessingWorkerTests
{
    [Fact]
    public async Task CycleProcessesEveryReplicatedPaymentSequentially()
    {
        var payments = new[]
        {
            CreateReplicated(Guid.NewGuid(), "WORKER-1"),
            CreateReplicated(Guid.NewGuid(), "WORKER-2"),
        };
        var processor = new RecordingProcessor();
        var cycle = CreateCycle(payments, processor);

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
        var cycle = CreateCycle(payments, processor);

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
            [CreateReplicated(Guid.NewGuid(), "WORKER-CANCEL")],
            new RecordingProcessor());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cycle.ExecuteAsync(cancellation.Token));
    }

    [Fact]
    public async Task BackgroundServiceCreatesOneScopePerCycle()
    {
        var scopeFactory = new CountingScopeFactory(() =>
            CreateCycle([], new RecordingProcessor()));
        var worker = new PaymentProcessingBackgroundService(
            scopeFactory,
            new PaymentProcessingOptions
            {
                ProcessingDurationMilliseconds = 8_000,
                LeaseDurationMilliseconds = 4_000,
                LeaseRenewalIntervalMilliseconds = 1_000,
                ScanIntervalMilliseconds = 500,
                BatchSize = 10,
            },
            TimeProvider.System);

        await worker.ExecuteCycleAsync(CancellationToken.None);
        await worker.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(2, scopeFactory.CreatedScopes);
        Assert.Equal(2, scopeFactory.DisposedScopes);
        Assert.Equal(2, scopeFactory.CreatedCycles);
    }

    [Fact]
    public async Task RepositoryReceivesConfiguredBatchSize()
    {
        var repository = new FakeRepository([]);
        var options = new PaymentProcessingOptions { BatchSize = 37 };
        var cycle = new PaymentProcessingCycle(
            repository,
            new RecordingProcessor(),
            new FakeIdGenerator(),
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                PaymentProcessingCycle>.Instance);

        await cycle.ExecuteAsync(CancellationToken.None);

        Assert.Equal(37, repository.RequestedLimit);
    }

    private static PaymentProcessingCycle CreateCycle(
        IReadOnlyCollection<Payment> payments,
        IPaymentProcessor processor) =>
        new(
            new FakeRepository(payments),
            processor,
            new FakeIdGenerator(),
            new PaymentProcessingOptions { BatchSize = 10 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                PaymentProcessingCycle>.Instance);

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

    private sealed class FakeRepository(
        IReadOnlyCollection<Payment> payments) : IPaymentRepository
    {
        public int RequestedLimit { get; private set; }

        public Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedLimit = limit;
            return Task.FromResult(payments);
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
