using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Domain.Payments.Events;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentLeaseTests
{
    [Fact]
    public void FirstClaimAssignsOwnershipWithoutIncrementingAttempt()
    {
        var payment = PaymentTestData.CreateReplicated();
        var lease = PaymentTestData.CreatedAt.AddMinutes(1);

        var applied = payment.Claim(
            PaymentTestData.NodeA,
            1,
            lease,
            PaymentTestData.CreatedAt.AddSeconds(2));

        Assert.True(applied);
        Assert.Equal(PaymentTestData.NodeA, payment.OwnerNodeId);
        Assert.Equal(1, payment.Term);
        Assert.Equal(lease, payment.LeaseExpiresAtUtc);
        Assert.Equal(0, payment.Attempt);
        Assert.Equal(3, payment.Version);
    }

    [Fact]
    public void ClaimWithTermZeroIsRejected()
    {
        var payment = PaymentTestData.CreateReplicated();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Claim(
                PaymentTestData.NodeA,
                0,
                PaymentTestData.CreatedAt.AddMinutes(1),
                PaymentTestData.CreatedAt.AddSeconds(2)));

        Assert.Equal(PaymentErrorCodes.PaymentTermStale, exception.Code);
    }

    [Fact]
    public void ClaimWithPastLeaseIsRejected()
    {
        var payment = PaymentTestData.CreateReplicated();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Claim(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt,
                PaymentTestData.CreatedAt.AddSeconds(2)));

        Assert.Equal(PaymentErrorCodes.PaymentLeaseInvalid, exception.Code);
    }

    [Fact]
    public void ActiveLeaseBlocksAnotherNode()
    {
        var payment = PaymentTestData.CreateClaimed();
        var version = payment.Version;

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Claim(
                PaymentTestData.NodeB,
                2,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddSeconds(30)));

        Assert.Equal(PaymentErrorCodes.PaymentLeaseActive, exception.Code);
        Assert.Equal(PaymentTestData.NodeA, payment.OwnerNodeId);
        Assert.Equal(version, payment.Version);
    }

    [Fact]
    public void ExpiredLeaseAllowsTakeover()
    {
        var payment = PaymentTestData.CreateClaimed();
        var occurredAt = PaymentTestData.CreatedAt.AddMinutes(1);
        var newLease = occurredAt.AddMinutes(1);

        var applied = payment.Claim(PaymentTestData.NodeB, 2, newLease, occurredAt);

        Assert.True(applied);
        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(PaymentTestData.NodeB, payment.OwnerNodeId);
        Assert.Equal(2, payment.Term);
        Assert.Equal(0, payment.Attempt);
        var claimed = Assert.IsType<PaymentClaimedDomainEvent>(
            payment.DequeueDomainEvents().Last());
        Assert.True(claimed.IsTakeover);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void TakeoverRequiresHigherTerm(long proposedTerm)
    {
        var payment = PaymentTestData.CreateClaimed();
        var version = payment.Version;
        var occurredAt = PaymentTestData.CreatedAt.AddMinutes(1);

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.Claim(
                PaymentTestData.NodeB,
                proposedTerm,
                occurredAt.AddMinutes(1),
                occurredAt));

        Assert.Equal(PaymentErrorCodes.PaymentTermStale, exception.Code);
        Assert.Equal(version, payment.Version);
        Assert.Equal(PaymentTestData.NodeA, payment.OwnerNodeId);
    }

    [Fact]
    public void ExactClaimReplayIsIdempotent()
    {
        var payment = PaymentTestData.CreateClaimed();
        payment.DequeueDomainEvents();
        var version = payment.Version;

        var applied = payment.Claim(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentTestData.CreatedAt.AddMinutes(2));

        Assert.False(applied);
        Assert.Equal(version, payment.Version);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void ValidRenewalExtendsLease()
    {
        var payment = PaymentTestData.CreateClaimed();
        var newLease = PaymentTestData.CreatedAt.AddMinutes(2);

        var applied = payment.RenewLease(
            PaymentTestData.NodeA,
            1,
            newLease,
            PaymentTestData.CreatedAt.AddSeconds(30));

        Assert.True(applied);
        Assert.Equal(newLease, payment.LeaseExpiresAtUtc);
        Assert.Equal(4, payment.Version);
        Assert.IsType<PaymentLeaseRenewedDomainEvent>(payment.DequeueDomainEvents().Last());
    }

    [Fact]
    public void EqualRenewalIsIdempotent()
    {
        var payment = PaymentTestData.CreateClaimed();
        payment.DequeueDomainEvents();
        var version = payment.Version;

        var applied = payment.RenewLease(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentTestData.CreatedAt.AddSeconds(30));

        Assert.False(applied);
        Assert.Equal(version, payment.Version);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void LeaseReductionIsRejected()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.RenewLease(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddSeconds(45),
                PaymentTestData.CreatedAt.AddSeconds(30)));

        Assert.Equal(PaymentErrorCodes.PaymentLeaseMustIncrease, exception.Code);
    }

    [Fact]
    public void RenewalByAnotherNodeIsRejected()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.RenewLease(
                PaymentTestData.NodeB,
                1,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddSeconds(30)));

        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, exception.Code);
    }

    [Fact]
    public void RenewalWithWrongTermIsRejected()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.RenewLease(
                PaymentTestData.NodeA,
                2,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddSeconds(30)));

        Assert.Equal(PaymentErrorCodes.PaymentTermMismatch, exception.Code);
    }

    [Fact]
    public void ExpiredLeaseCannotBeRenewed()
    {
        var payment = PaymentTestData.CreateClaimed();

        var exception = Assert.Throws<PaymentDomainException>(
            () => payment.RenewLease(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddMinutes(1)));

        Assert.Equal(PaymentErrorCodes.PaymentLeaseExpired, exception.Code);
    }

    [Fact]
    public void LeaseQueriesRespectExpirationBoundary()
    {
        var payment = PaymentTestData.CreateClaimed();
        var beforeExpiration = PaymentTestData.CreatedAt.AddSeconds(59);
        var atExpiration = PaymentTestData.CreatedAt.AddMinutes(1);

        Assert.True(payment.HasActiveLease(beforeExpiration));
        Assert.False(payment.IsLeaseExpired(beforeExpiration));
        Assert.False(payment.CanBeTakenOver(beforeExpiration));
        Assert.False(payment.HasActiveLease(atExpiration));
        Assert.True(payment.IsLeaseExpired(atExpiration));
        Assert.True(payment.CanBeTakenOver(atExpiration));
    }

    [Fact]
    public void ProcessingTakeoverPreservesAttempt()
    {
        var payment = PaymentTestData.CreateProcessing();
        var occurredAt = PaymentTestData.CreatedAt.AddMinutes(1);

        payment.Claim(PaymentTestData.NodeB, 2, occurredAt.AddMinutes(1), occurredAt);

        Assert.Equal(PaymentStatus.Claimed, payment.Status);
        Assert.Equal(1, payment.Attempt);
    }
}
