using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Domain.Payments.Events;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentCreationTests
{
    [Fact]
    public void CreationSetsInitialProperties()
    {
        var payment = PaymentTestData.CreateReceived();

        Assert.Equal(PaymentTestData.PaymentId, payment.Id);
        Assert.Equal(new IdempotencyKey("PAY-2026-0001"), payment.IdempotencyKey);
        Assert.Equal(new Money(150_000m, "COP"), payment.Amount);
        Assert.Equal(PaymentStatus.Received, payment.Status);
        Assert.Equal(0, payment.Term);
        Assert.Equal(0, payment.Attempt);
        Assert.Equal(1, payment.Version);
        Assert.Null(payment.OwnerNodeId);
        Assert.Null(payment.LeaseExpiresAtUtc);
        Assert.Null(payment.CompletedAtUtc);
        Assert.False(payment.IsTerminal);
    }

    [Fact]
    public void EmptyIdIsRejected()
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => Payment.Create(
                Guid.Empty,
                new IdempotencyKey("key"),
                new Money(10m, "COP"),
                PaymentTestData.CreatedAt));

        Assert.Equal(PaymentErrorCodes.PaymentIdRequired, exception.Code);
    }

    [Fact]
    public void NullIdempotencyKeyIsRejected()
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => Payment.Create(
                PaymentTestData.PaymentId,
                null,
                new Money(10m, "COP"),
                PaymentTestData.CreatedAt));

        Assert.Equal(PaymentErrorCodes.IdempotencyKeyRequired, exception.Code);
    }

    [Fact]
    public void NullAmountIsRejected()
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => Payment.Create(
                PaymentTestData.PaymentId,
                new IdempotencyKey("key"),
                null,
                PaymentTestData.CreatedAt));

        Assert.Equal(PaymentErrorCodes.PaymentAmountRequired, exception.Code);
    }

    [Fact]
    public void CreationTimeIsConvertedToUtc()
    {
        var localTime = new DateTimeOffset(2026, 7, 23, 7, 0, 0, TimeSpan.FromHours(-5));

        var payment = PaymentTestData.CreateReceived(createdAt: localTime);

        Assert.Equal(TimeSpan.Zero, payment.CreatedAtUtc.Offset);
        Assert.Equal(localTime.ToUniversalTime(), payment.CreatedAtUtc);
        Assert.Equal(payment.CreatedAtUtc, payment.UpdatedAtUtc);
    }

    [Fact]
    public void CreationEmitsVersionOneEvent()
    {
        var payment = PaymentTestData.CreateReceived();

        var created = Assert.IsType<PaymentCreatedDomainEvent>(
            Assert.Single(payment.DequeueDomainEvents()));

        Assert.Equal(payment.Id, created.PaymentId);
        Assert.Equal(1, created.Version);
        Assert.Equal(payment.CreatedAtUtc, created.OccurredAtUtc);
        Assert.Equal(payment.IdempotencyKey, created.IdempotencyKey);
        Assert.Equal(payment.Amount, created.Amount);
    }

    [Fact]
    public void DequeueClearsDomainEvents()
    {
        var payment = PaymentTestData.CreateReceived();

        var firstRead = payment.DequeueDomainEvents();
        var secondRead = payment.DequeueDomainEvents();

        Assert.Single(firstRead);
        Assert.Empty(secondRead);
    }

    [Fact]
    public void DequeuedCollectionCannotModifyAggregateEvents()
    {
        var payment = PaymentTestData.CreateReceived();
        var events = payment.DequeueDomainEvents();
        var collection = Assert.IsAssignableFrom<ICollection<IDomainEvent>>(events);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Empty(payment.DequeueDomainEvents());
    }
}
