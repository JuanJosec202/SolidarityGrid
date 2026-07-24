using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentTakeoverFencingTests
{
    [Fact]
    public void OldOwnerCannotStartRenewOrCompleteAfterHigherTermTakeover()
    {
        var oldOwner = new NodeId("node-a");
        var newOwner = new NodeId("node-b");
        var payment = PaymentTestData.CreateProcessing(
            oldOwner,
            term: 1,
            leaseExpiresAt: PaymentTestData.CreatedAt.AddSeconds(5));
        payment.Claim(
            newOwner,
            proposedTerm: 2,
            leaseExpiresAtUtc: PaymentTestData.CreatedAt.AddMinutes(1),
            occurredAtUtc: PaymentTestData.CreatedAt.AddSeconds(6));
        var versionAfterTakeover = payment.Version;

        var staleStart = Assert.Throws<PaymentDomainException>(() =>
            payment.StartProcessing(
                oldOwner,
                1,
                PaymentTestData.CreatedAt.AddSeconds(7)));
        var staleRenew = Assert.Throws<PaymentDomainException>(() =>
            payment.RenewLease(
                oldOwner,
                1,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddSeconds(7)));

        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, staleStart.Code);
        Assert.Equal(PaymentErrorCodes.PaymentOwnerMismatch, staleRenew.Code);
        Assert.Equal(versionAfterTakeover, payment.Version);
        Assert.Equal(newOwner, payment.OwnerNodeId);
        Assert.Equal(2, payment.Term);
        Assert.Equal(1, payment.Attempt);

        payment.StartProcessing(
            newOwner,
            2,
            PaymentTestData.CreatedAt.AddSeconds(8));
        var versionAfterRestart = payment.Version;
        var staleCompletion = Assert.Throws<PaymentDomainException>(() =>
            payment.Complete(
                oldOwner,
                1,
                PaymentTestData.CreatedAt.AddSeconds(9)));

        Assert.Equal(
            PaymentErrorCodes.PaymentOwnerMismatch,
            staleCompletion.Code);
        Assert.Equal(versionAfterRestart, payment.Version);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(newOwner, payment.OwnerNodeId);
        Assert.Equal(2, payment.Term);
        Assert.Equal(2, payment.Attempt);
        Assert.Null(payment.CompletedAtUtc);
    }

    [Fact]
    public void StaleTermCannotMutateCurrentOwnerState()
    {
        var owner = new NodeId("node-b");
        var payment = PaymentTestData.CreateProcessing(
            new NodeId("node-a"),
            term: 1,
            leaseExpiresAt: PaymentTestData.CreatedAt.AddSeconds(5));
        payment.Claim(
            owner,
            2,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentTestData.CreatedAt.AddSeconds(6));
        payment.StartProcessing(
            owner,
            2,
            PaymentTestData.CreatedAt.AddSeconds(7));
        var version = payment.Version;

        var renewal = Assert.Throws<PaymentDomainException>(() =>
            payment.RenewLease(
                owner,
                1,
                PaymentTestData.CreatedAt.AddMinutes(2),
                PaymentTestData.CreatedAt.AddSeconds(8)));
        var completion = Assert.Throws<PaymentDomainException>(() =>
            payment.Complete(
                owner,
                1,
                PaymentTestData.CreatedAt.AddSeconds(8)));

        Assert.Equal(PaymentErrorCodes.PaymentTermMismatch, renewal.Code);
        Assert.Equal(PaymentErrorCodes.PaymentTermMismatch, completion.Code);
        Assert.Equal(version, payment.Version);
        Assert.Equal(2, payment.Term);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }
}
