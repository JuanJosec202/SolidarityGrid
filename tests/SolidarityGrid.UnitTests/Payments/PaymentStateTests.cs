using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Domain.Payments.Events;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentStateTests
{
    [Fact]
    public void ReceivedTransitionsToReplicated()
    {
        var payment = PaymentTestData.CreateReceived();

        var applied = payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(1));

        Assert.True(applied);
        Assert.Equal(PaymentStatus.Replicated, payment.Status);
        Assert.Equal(2, payment.Version);
        Assert.Equal(
            PaymentTestData.CreatedAt.AddSeconds(1),
            payment.UpdatedAtUtc);
        Assert.IsType<PaymentReplicatedDomainEvent>(payment.DequeueDomainEvents().Last());
    }

    [Fact]
    public void ReplicatedTransitionsToClaimed()
    {
        var payment = PaymentTestData.CreateReplicated();

        payment.Claim(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentTestData.CreatedAt.AddSeconds(2));

        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(3, payment.Version);
    }

    [Fact]
    public void ClaimedTransitionsToProcessing()
    {
        var payment = PaymentTestData.CreateClaimed();

        payment.StartProcessing(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(3));

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(4, payment.Version);
    }

    [Fact]
    public void ProcessingTransitionsToCompleted()
    {
        var payment = PaymentTestData.CreateProcessing();

        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(4));

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(5, payment.Version);
    }

    [Fact]
    public void ReceivedCannotBeClaimed()
    {
        var payment = PaymentTestData.CreateReceived();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Claim(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddMinutes(1),
                PaymentTestData.CreatedAt.AddSeconds(1)));

        Assert.Equal(PaymentErrorCodes.PaymentNotReplicated, exception.Code);
        Assert.Equal(PaymentStatus.Received, payment.Status);
        Assert.Equal(1, payment.Version);
    }

    [Fact]
    public void ReplicatedCannotStartProcessing()
    {
        var payment = PaymentTestData.CreateReplicated();
        var version = payment.Version;

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddSeconds(2)));

        Assert.Equal(PaymentErrorCodes.PaymentInvalidState, exception.Code);
        Assert.Equal(PaymentStatus.Replicated, payment.Status);
        Assert.Equal(version, payment.Version);
    }

    [Fact]
    public void ClaimedCannotComplete()
    {
        var payment = PaymentTestData.CreateClaimed();
        var version = payment.Version;

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Complete(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddSeconds(3)));

        Assert.Equal(PaymentErrorCodes.PaymentInvalidState, exception.Code);
        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(version, payment.Version);
    }

    [Fact]
    public void CompletedRejectsNewClaimsAndLeaseRenewals()
    {
        var payment = CreateCompletedPayment();
        var version = payment.Version;

        var claimException = Assert.Throws<PaymentDomainException>(
            () => payment.Claim(
                PaymentTestData.NodeB,
                2,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddMinutes(1)));
        var renewalException = Assert.Throws<PaymentDomainException>(
            () => payment.RenewLease(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddSeconds(5)));

        Assert.Equal(PaymentErrorCodes.PaymentAlreadyCompleted, claimException.Code);
        Assert.Equal(PaymentErrorCodes.PaymentAlreadyCompleted, renewalException.Code);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(version, payment.Version);
    }

    [Fact]
    public void CompletedCannotStartProcessingAgain()
    {
        var payment = CreateCompletedPayment();
        var version = payment.Version;

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddSeconds(5)));

        Assert.Equal(PaymentErrorCodes.PaymentAlreadyCompleted, exception.Code);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(version, payment.Version);
    }

    [Fact]
    public void MarkReplicatedAfterReplicationIsIdempotent()
    {
        var payment = PaymentTestData.CreateReplicated();
        payment.DequeueDomainEvents();
        var version = payment.Version;

        var applied = payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(10));

        Assert.False(applied);
        Assert.Equal(version, payment.Version);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    private static Payment CreateCompletedPayment()
    {
        var payment = PaymentTestData.CreateProcessing();
        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(4));
        return payment;
    }
}
