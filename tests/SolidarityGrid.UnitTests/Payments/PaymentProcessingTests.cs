using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Domain.Payments.Events;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentProcessingTests
{
    [Fact]
    public void OwnerCanStartProcessingWithCurrentTerm()
    {
        var payment = PaymentTestData.CreateClaimed();

        var applied = payment.StartProcessing(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(3));

        Assert.True(applied);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(1, payment.Attempt);
        Assert.Equal(4, payment.Version);
        var started = Assert.IsType<PaymentProcessingStartedDomainEvent>(
            payment.DequeueDomainEvents().Last());
        Assert.Equal(1, started.Attempt);
    }

    [Fact]
    public void WrongOwnerCannotStartProcessing()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeB,
                1,
                PaymentTestData.CreatedAt.AddSeconds(3)));

        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, exception.Code);
        Assert.Equal(0, payment.Attempt);
    }

    [Fact]
    public void WrongTermCannotStartProcessing()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeA,
                2,
                PaymentTestData.CreatedAt.AddSeconds(3)));

        Assert.Equal(PaymentErrorCodes.PaymentTermMismatch, exception.Code);
        Assert.Equal(0, payment.Attempt);
    }

    [Fact]
    public void ExpiredLeaseCannotStartProcessing()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddMinutes(1)));

        Assert.Equal(PaymentErrorCodes.PaymentLeaseExpired, exception.Code);
        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(0, payment.Attempt);
    }

    [Fact]
    public void RepeatedStartDoesNotIncrementAttemptOrVersion()
    {
        var payment = PaymentTestData.CreateProcessing();
        payment.DequeueDomainEvents();
        var version = payment.Version;

        var applied = payment.StartProcessing(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(10));

        Assert.False(applied);
        Assert.Equal(1, payment.Attempt);
        Assert.Equal(version, payment.Version);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void ProcessingReplayWithAnotherOwnerIsRejected()
    {
        var payment = PaymentTestData.CreateProcessing();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeB,
                1,
                PaymentTestData.CreatedAt.AddSeconds(10)));

        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, exception.Code);
    }

    [Fact]
    public void ProcessingReplayWithAnotherTermIsRejected()
    {
        var payment = PaymentTestData.CreateProcessing();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeA,
                2,
                PaymentTestData.CreatedAt.AddSeconds(10)));

        Assert.Equal(PaymentErrorCodes.PaymentTermMismatch, exception.Code);
    }

    [Fact]
    public void TakeoverFollowedByProcessingIncrementsAttemptAgain()
    {
        var payment = PaymentTestData.CreateProcessing();
        var takeoverAt = PaymentTestData.CreatedAt.AddMinutes(1);

        payment.Claim(
            PaymentTestData.NodeB,
            2,
            takeoverAt.AddMinutes(1),
            takeoverAt);
        payment.StartProcessing(
            PaymentTestData.NodeB,
            2,
            takeoverAt.AddSeconds(1));

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(2, payment.Attempt);
        Assert.Equal(2, payment.Term);
        Assert.Equal(PaymentTestData.NodeB, payment.OwnerNodeId);
    }

    [Fact]
    public void ClaimAndRenewalDoNotIncrementAttempt()
    {
        var payment = PaymentTestData.CreateClaimed();

        payment.RenewLease(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(2),
            PaymentTestData.CreatedAt.AddSeconds(30));

        Assert.Equal(0, payment.Attempt);
    }
}
