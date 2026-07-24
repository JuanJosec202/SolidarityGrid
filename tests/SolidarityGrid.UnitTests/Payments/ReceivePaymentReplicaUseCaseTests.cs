using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class ReceivePaymentReplicaUseCaseTests
{
    private static readonly Guid PaymentId =
        Guid.Parse("d510a91d-60a2-4f4c-9e16-1c037f0ad001");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReplicatedAt =
        CreatedAt.AddSeconds(2);

    [Fact]
    public async Task NewReplicaIsStoredWithPreservedIdentityAndTimestamp()
    {
        var repository = new FakeRepository();
        var unitOfWork = new FakeUnitOfWork();
        var useCase = new ReceivePaymentReplicaUseCase(repository, unitOfWork);

        var result = await useCase.ExecuteAsync(
            Replica(),
            CancellationToken.None);

        var stored = Assert.IsType<Payment>(repository.Added);
        Assert.True(result.Stored);
        Assert.False(result.AlreadyExisted);
        Assert.Equal(PaymentStatus.Replicated, stored.Status);
        Assert.Equal(PaymentId, stored.Id);
        Assert.Equal("REPLICA-1", stored.IdempotencyKey.Value);
        Assert.Equal(new Money(125.50m, "USD"), stored.Amount);
        Assert.Equal(CreatedAt, stored.CreatedAtUtc);
        Assert.Equal(ReplicatedAt, stored.UpdatedAtUtc);
        Assert.Equal(2, stored.Version);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ExactReplayIsIdempotentAndDoesNotSaveAgain()
    {
        var existing = CreatePayment();
        existing.MarkReplicated(ReplicatedAt);
        existing.DequeueDomainEvents();
        var repository = new FakeRepository(existing);
        var unitOfWork = new FakeUnitOfWork();
        var useCase = new ReceivePaymentReplicaUseCase(repository, unitOfWork);
        var version = existing.Version;
        var updatedAt = existing.UpdatedAtUtc;

        var result = await useCase.ExecuteAsync(
            Replica(),
            CancellationToken.None);

        Assert.True(result.Stored);
        Assert.True(result.AlreadyExisted);
        Assert.Equal(version, existing.Version);
        Assert.Equal(updatedAt, existing.UpdatedAtUtc);
        Assert.Equal(0, unitOfWork.SaveCalls);
        Assert.Empty(existing.DequeueDomainEvents());
    }

    [Fact]
    public async Task ExistingReceivedPaymentBecomesReplicated()
    {
        var existing = CreatePayment();
        var unitOfWork = new FakeUnitOfWork();
        var useCase = new ReceivePaymentReplicaUseCase(
            new FakeRepository(existing),
            unitOfWork);

        var result = await useCase.ExecuteAsync(
            Replica(),
            CancellationToken.None);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(PaymentStatus.Replicated, existing.Status);
        Assert.Equal(2, existing.Version);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task DifferentPayloadForSameIdConflicts()
    {
        var existing = CreatePayment();
        var unitOfWork = new FakeUnitOfWork();
        var useCase = new ReceivePaymentReplicaUseCase(
            new FakeRepository(existing),
            unitOfWork);

        var result = await useCase.ExecuteAsync(
            Replica(amount: 999m),
            CancellationToken.None);

        Assert.False(result.Stored);
        Assert.Equal(PaymentReplicaErrorCodes.Conflict, result.ErrorCode);
        Assert.Equal(PaymentStatus.Received, existing.Status);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task SameKeyWithDifferentIdConflicts()
    {
        var existing = CreatePayment();
        var useCase = new ReceivePaymentReplicaUseCase(
            new FakeRepository(existing),
            new FakeUnitOfWork());

        var result = await useCase.ExecuteAsync(
            Replica(paymentId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.Stored);
        Assert.Equal(PaymentReplicaErrorCodes.Conflict, result.ErrorCode);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var useCase = new ReceivePaymentReplicaUseCase(
            new FakeRepository(),
            new FakeUnitOfWork());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(Replica(), cancellation.Token));
    }

    private static PaymentReplica Replica(
        Guid? paymentId = null,
        decimal amount = 125.50m) =>
        new(
            paymentId ?? PaymentId,
            new IdempotencyKey("REPLICA-1"),
            new Money(amount, "USD"),
            CreatedAt,
            ReplicatedAt);

    private static Payment CreatePayment() =>
        Payment.Create(
            PaymentId,
            new IdempotencyKey("REPLICA-1"),
            new Money(125.50m, "USD"),
            CreatedAt);

    private sealed class FakeRepository(Payment? existing = null)
        : IPaymentRepository
    {
        public Payment? Added { get; private set; }

        public Task<Payment?> GetByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                existing?.Id == paymentId ? existing : null);
        }

        public Task<Payment?> GetByIdempotencyKeyAsync(
            IdempotencyKey idempotencyKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                existing?.IdempotencyKey == idempotencyKey ? existing : null);
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
            Added = payment;
            return ValueTask.CompletedTask;
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
}
