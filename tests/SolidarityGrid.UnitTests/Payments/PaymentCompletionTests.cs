using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Domain.Payments.Events;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentCompletionTests
{
    [Fact]
    public void OwnerCanCompleteProcessingPayment()
    {
        var payment = PaymentTestData.CreateProcessing();
        var completedAt = PaymentTestData.CreatedAt.AddSeconds(4);

        var applied = payment.Complete(PaymentTestData.NodeA, 1, completedAt);

        Assert.True(applied);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(completedAt, payment.CompletedAtUtc);
        Assert.Null(payment.LeaseExpiresAtUtc);
        Assert.True(payment.IsTerminal);
        Assert.Equal(5, payment.Version);
    }

    [Fact]
    public void WrongOwnerCannotCompletePayment()
    {
        var payment = PaymentTestData.CreateProcessing();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Complete(
                PaymentTestData.NodeB,
                1,
                PaymentTestData.CreatedAt.AddSeconds(4)));

        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, exception.Code);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }

    [Fact]
    public void WrongTermCannotCompletePayment()
    {
        var payment = PaymentTestData.CreateProcessing();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Complete(
                PaymentTestData.NodeA,
                2,
                PaymentTestData.CreatedAt.AddSeconds(4)));

        Assert.Equal(PaymentErrorCodes.PaymentTermMismatch, exception.Code);
    }

    [Fact]
    public void ExpiredLeaseCannotCompletePayment()
    {
        var payment = PaymentTestData.CreateProcessing();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Complete(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddMinutes(1)));

        Assert.Equal(PaymentErrorCodes.PaymentLeaseExpired, exception.Code);
        Assert.Null(payment.CompletedAtUtc);
    }

    [Fact]
    public void IncorrectStateCannotCompletePayment()
    {
        var payment = PaymentTestData.CreateReplicated();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Complete(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddSeconds(2)));

        Assert.Equal(PaymentErrorCodes.PaymentInvalidState, exception.Code);
    }

    [Fact]
    public void CompletionTimeIsConvertedToUtc()
    {
        var payment = PaymentTestData.CreateProcessing(
            leaseExpiresAt: PaymentTestData.CreatedAt.AddHours(2));
        var localTime = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(-5));

        payment.Complete(PaymentTestData.NodeA, 1, localTime);

        Assert.Equal(localTime.ToUniversalTime(), payment.CompletedAtUtc);
        Assert.Equal(TimeSpan.Zero, payment.CompletedAtUtc?.Offset);
    }

    [Fact]
    public void RepeatedCompletionIsIdempotent()
    {
        var payment = PaymentTestData.CreateProcessing();
        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(4));
        payment.DequeueDomainEvents();
        var version = payment.Version;
        var completedAt = payment.CompletedAtUtc;

        var applied = payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(5));

        Assert.False(applied);
        Assert.Equal(version, payment.Version);
        Assert.Equal(completedAt, payment.CompletedAtUtc);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void RepeatedCompletionRejectsDifferentOwnerOrTerm()
    {
        var payment = PaymentTestData.CreateProcessing();
        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(4));

        var exception = Assert.Throws<PaymentDomainException>(() =>
            payment.Complete(
                PaymentTestData.NodeB,
                99,
                PaymentTestData.CreatedAt.AddMinutes(5)));

        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, exception.Code);
    }

    [Fact]
    public void CompletionEventContainsOwnershipData()
    {
        var payment = PaymentTestData.CreateProcessing();

        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(4));

        var completed = Assert.IsType<PaymentCompletedDomainEvent>(
            payment.DequeueDomainEvents().Last());
        Assert.Equal(PaymentTestData.NodeA, completed.OwnerNodeId);
        Assert.Equal(1, completed.Term);
        Assert.Equal(1, completed.Attempt);
        Assert.Equal(5, completed.Version);
    }
}
