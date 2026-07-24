using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class GetPaymentByIdUseCaseTests
{
    [Fact]
    public async Task ExistingPaymentReturnsNeutralDtoWithoutMutation()
    {
        var payment = CreatePayment();
        payment.DequeueDomainEvents();
        var repository = new FakeRepository(payment);
        var useCase = new GetPaymentByIdUseCase(repository);

        var result = await useCase.ExecuteAsync(
            payment.Id,
            CancellationToken.None);

        Assert.Equal(payment.Id, result?.Id);
        Assert.Equal("Received", result?.Status);
        Assert.Empty(payment.DequeueDomainEvents());
        Assert.Equal(1, repository.IdQueries);
    }

    [Fact]
    public async Task MissingPaymentReturnsNull()
    {
        var useCase = new GetPaymentByIdUseCase(new FakeRepository(null));

        var result = await useCase.ExecuteAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EmptyIdReturnsNullWithoutQuery()
    {
        var repository = new FakeRepository(null);
        var useCase = new GetPaymentByIdUseCase(repository);

        var result = await useCase.ExecuteAsync(
            Guid.Empty,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.IdQueries);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        var useCase = new GetPaymentByIdUseCase(
            new FakeRepository(CreatePayment()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), cancellation.Token));
    }

    private static Payment CreatePayment() =>
        Payment.Create(
            Guid.Parse("22222222-3333-4444-5555-666666666666"),
            new IdempotencyKey("GET-1"),
            new Money(10m, "USD"),
            new DateTimeOffset(2026, 7, 24, 1, 2, 3, TimeSpan.Zero));

    private sealed class FakeRepository(Payment? payment) : IPaymentRepository
    {
        public int IdQueries { get; private set; }

        public Task<Payment?> GetByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            IdQueries++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(payment?.Id == paymentId ? payment : null);
        }

        public Task<Payment?> GetByIdempotencyKeyAsync(
            IdempotencyKey idempotencyKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask AddAsync(
            Payment paymentToAdd,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
