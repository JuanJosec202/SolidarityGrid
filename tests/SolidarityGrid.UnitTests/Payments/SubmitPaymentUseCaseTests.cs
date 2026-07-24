using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class SubmitPaymentUseCaseTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 7, 24, 10, 11, 12, TimeSpan.Zero);
    private static readonly Guid GeneratedId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task NewPaymentUsesInjectedIdAndClockAndSavesOnce()
    {
        var fixture = CreateFixture();

        var result = await fixture.UseCase.ExecuteAsync(
            Command(),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Created, result.Outcome);
        Assert.Equal(GeneratedId, result.Payment?.Id);
        Assert.Equal(UtcNow, result.Payment?.CreatedAtUtc);
        Assert.Equal("Replicated", result.Payment?.Status);
        Assert.Equal(2, result.Payment?.Version);
        Assert.Equal("COP", result.Payment?.Currency);
        Assert.Equal(1, fixture.Repository.AddCalls);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(1, fixture.IdGenerator.Calls);
    }

    [Fact]
    public async Task ReplayReturnsExistingPaymentWithoutMutationOrSave()
    {
        var existing = ExistingPayment(replicated: true);
        existing.DequeueDomainEvents();
        var version = existing.Version;
        var updatedAt = existing.UpdatedAtUtc;
        var fixture = CreateFixture(existing);

        var result = await fixture.UseCase.ExecuteAsync(
            Command(),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Replayed, result.Outcome);
        Assert.Equal(existing.Id, result.Payment?.Id);
        Assert.Equal(version, existing.Version);
        Assert.Equal(updatedAt, existing.UpdatedAtUtc);
        Assert.Empty(existing.DequeueDomainEvents());
        Assert.Equal(0, fixture.Repository.AddCalls);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(0, fixture.IdGenerator.Calls);
    }

    [Theory]
    [InlineData(151000, "COP")]
    [InlineData(150000, "USD")]
    public async Task ReusedKeyWithDifferentPayloadConflicts(
        decimal amount,
        string currency)
    {
        var fixture = CreateFixture(ExistingPayment());

        var result = await fixture.UseCase.ExecuteAsync(
            Command(amount: amount, currency: currency),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Conflict, result.Outcome);
        Assert.Equal(
            PaymentApplicationErrorCodes.IdempotencyKeyPayloadConflict,
            result.ErrorCode);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IdempotencyKeyLookupIsCaseSensitive()
    {
        var fixture = CreateFixture();

        var result = await fixture.UseCase.ExecuteAsync(
            Command(key: "pay-1"),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Created, result.Outcome);
        Assert.Equal("pay-1", fixture.Repository.LastRequestedKey?.Value);
    }

    [Theory]
    [InlineData("", 150000, "COP", PaymentErrorCodes.IdempotencyKeyRequired)]
    [InlineData("PAY-1", 0, "COP", PaymentErrorCodes.PaymentAmountMustBePositive)]
    [InlineData("PAY-1", -1, "COP", PaymentErrorCodes.PaymentAmountMustBePositive)]
    [InlineData("PAY-1", 150000, "", PaymentErrorCodes.PaymentCurrencyInvalid)]
    [InlineData("PAY-1", 150000, "CO", PaymentErrorCodes.PaymentCurrencyInvalid)]
    public async Task InvalidInputReturnsStableDomainCode(
        string key,
        decimal amount,
        string currency,
        string expectedCode)
    {
        var fixture = CreateFixture();

        var result = await fixture.UseCase.ExecuteAsync(
            Command(key, amount, currency),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Invalid, result.Outcome);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(0, fixture.Repository.AddCalls);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task CurrencyIsNormalizedBeforePersistence()
    {
        var fixture = CreateFixture();

        var result = await fixture.UseCase.ExecuteAsync(
            Command(currency: "cop"),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal("COP", result.Payment?.Currency);
        Assert.Equal("COP", fixture.Repository.AddedPayment?.Amount.Currency);
    }

    [Fact]
    public async Task DuplicateInsertWithMatchingWinnerReturnsReplay()
    {
        var winner = ExistingPayment(replicated: true);
        var duplicate = new DuplicatePaymentIdempotencyKeyException(
            winner.IdempotencyKey,
            new InvalidOperationException("simulated unique conflict"));
        var fixture = CreateFixture(
            responses: [null, winner],
            saveException: duplicate);

        var result = await fixture.UseCase.ExecuteAsync(
            Command(),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Replayed, result.Outcome);
        Assert.Equal(winner.Id, result.Payment?.Id);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(2, fixture.Repository.KeyQueryCalls);
    }

    [Fact]
    public async Task DuplicateInsertWithDifferentWinnerReturnsConflict()
    {
        var winner = ExistingPayment(amount: 999m, replicated: true);
        var duplicate = new DuplicatePaymentIdempotencyKeyException(
            winner.IdempotencyKey,
            new InvalidOperationException("simulated unique conflict"));
        var fixture = CreateFixture(
            responses: [null, winner],
            saveException: duplicate);

        var result = await fixture.UseCase.ExecuteAsync(
            Command(),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Conflict, result.Outcome);
        Assert.Equal(
            PaymentApplicationErrorCodes.IdempotencyKeyPayloadConflict,
            result.ErrorCode);
    }

    [Fact]
    public async Task MissingWinnerAfterDuplicateRaisesClearPersistenceException()
    {
        var key = new IdempotencyKey("PAY-1");
        var duplicate = new DuplicatePaymentIdempotencyKeyException(
            key,
            new InvalidOperationException("simulated unique conflict"));
        var fixture = CreateFixture(
            responses: [null, null],
            saveException: duplicate);

        var exception = await Assert.ThrowsAsync<PaymentIdempotencyRaceException>(
            () => fixture.UseCase.ExecuteAsync(
                Command(),
                "test-correlation",
                CancellationToken.None));

        Assert.Equal(key, exception.IdempotencyKey);
    }

    [Fact]
    public async Task CancellationTokenIsPropagatedToRepository()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.UseCase.ExecuteAsync(
                Command(),
                "test-correlation",
                cancellation.Token));

        Assert.Equal(cancellation.Token, fixture.Repository.LastCancellationToken);
    }

    [Fact]
    public async Task QuorumUnavailableLeavesNewPaymentReceived()
    {
        var fixture = CreateFixture(
            replicaResults:
            [
                FailedReplica("peer-one"),
                FailedReplica("peer-two"),
            ]);

        var result = await fixture.UseCase.ExecuteAsync(
            Command(),
            "no-quorum",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.ReplicationUnavailable, result.Outcome);
        Assert.Equal("Received", result.Payment?.Status);
        Assert.Equal(1, result.Payment?.Version);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ReceivedReplayRetriesAndBecomesReplicated()
    {
        var existing = ExistingPayment();
        var fixture = CreateFixture(existing);

        var result = await fixture.UseCase.ExecuteAsync(
            Command(),
            "retry-replication",
            CancellationToken.None);

        Assert.Equal(SubmitPaymentOutcome.Replayed, result.Outcome);
        Assert.Equal(existing.Id, result.Payment?.Id);
        Assert.Equal("Replicated", result.Payment?.Status);
        Assert.Equal(2, result.Payment?.Version);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    private static Fixture CreateFixture(
        Payment? existing = null,
        IEnumerable<Payment?>? responses = null,
        Exception? saveException = null,
        IReadOnlyCollection<PaymentReplicaResult>? replicaResults = null)
    {
        var repository = new FakePaymentRepository(existing, responses);
        var unitOfWork = new FakeUnitOfWork(saveException);
        var idGenerator = new FixedIdGenerator(GeneratedId);
        var clock = new FixedTimeProvider(UtcNow);
        var coordinator = new PaymentReplicationCoordinator(
            new FakeReplicaTransport(replicaResults),
            unitOfWork,
            clock,
            new NullReplicationObserver());
        return new Fixture(
            new SubmitPaymentUseCase(
                repository,
                unitOfWork,
                idGenerator,
                clock,
                coordinator),
            repository,
            unitOfWork,
            idGenerator);
    }

    private static SubmitPaymentCommand Command(
        string key = "PAY-1",
        decimal amount = 150000m,
        string currency = "COP") =>
        new(key, amount, currency);

    private static PaymentReplicaResult FailedReplica(string nodeId) =>
        new(
            new NodeId(nodeId),
            false,
            false,
            PaymentReplicaErrorCodes.PeerUnavailable,
            "Unavailable.");

    private static Payment ExistingPayment(
        decimal amount = 150000m,
        bool replicated = false)
    {
        var payment = Payment.Create(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            new IdempotencyKey("PAY-1"),
            new Money(amount, "COP"),
            UtcNow.AddMinutes(-1));
        if (replicated)
        {
            payment.MarkReplicated(UtcNow.AddSeconds(-30));
        }

        return payment;
    }

    private sealed record Fixture(
        SubmitPaymentUseCase UseCase,
        FakePaymentRepository Repository,
        FakeUnitOfWork UnitOfWork,
        FixedIdGenerator IdGenerator);

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public int Calls { get; private set; }

        public Guid NewId()
        {
            Calls++;
            return id;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Payment? _existing;
        private readonly Queue<Payment?> _responses;

        public FakePaymentRepository(
            Payment? existing,
            IEnumerable<Payment?>? responses)
        {
            _existing = existing;
            _responses = new Queue<Payment?>(responses ?? []);
        }

        public int AddCalls { get; private set; }

        public int KeyQueryCalls { get; private set; }

        public Payment? AddedPayment { get; private set; }

        public IdempotencyKey? LastRequestedKey { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Payment?> GetByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _existing?.Id == paymentId ? _existing : null);
        }

        public Task<Payment?> GetByIdempotencyKeyAsync(
            IdempotencyKey idempotencyKey,
            CancellationToken cancellationToken)
        {
            LastRequestedKey = idempotencyKey;
            LastCancellationToken = cancellationToken;
            KeyQueryCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _responses.Count > 0 ? _responses.Dequeue() : _existing);
        }

        public Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask AddAsync(
            Payment payment,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddCalls++;
            AddedPayment = payment;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork(Exception? saveException) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            return saveException is null
                ? Task.FromResult(1)
                : Task.FromException<int>(saveException);
        }
    }

    private sealed class FakeReplicaTransport(
        IReadOnlyCollection<PaymentReplicaResult>? results) : IPaymentReplicaTransport
    {
        public Task<IReadOnlyCollection<PaymentReplicaResult>> ReplicateAsync(
            PaymentReplica replica,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyCollection<PaymentReplicaResult> actual = results ??
            [
                new(
                    new NodeId("peer-one"),
                    true,
                    false,
                    null,
                    null),
                new(
                    new NodeId("peer-two"),
                    false,
                    false,
                    PaymentReplicaErrorCodes.PeerUnavailable,
                    "Unavailable."),
            ];
            return Task.FromResult(actual);
        }
    }

    private sealed class NullReplicationObserver : IPaymentReplicationObserver
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
