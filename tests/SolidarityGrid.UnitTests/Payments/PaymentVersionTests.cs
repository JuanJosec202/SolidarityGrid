using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentVersionTests
{
    [Fact]
    public void AppliedTransitionsIncrementVersionExactlyOnce()
    {
        var payment = PaymentTestData.CreateReceived();

        payment.MarkReplicated(PaymentTestData.CreatedAt.AddSeconds(1));
        Assert.Equal(2, payment.Version);

        payment.Claim(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(1),
            PaymentTestData.CreatedAt.AddSeconds(2));
        Assert.Equal(3, payment.Version);

        payment.StartProcessing(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(3));
        Assert.Equal(4, payment.Version);

        payment.RenewLease(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddMinutes(2),
            PaymentTestData.CreatedAt.AddSeconds(4));
        Assert.Equal(5, payment.Version);

        payment.Complete(
            PaymentTestData.NodeA,
            1,
            PaymentTestData.CreatedAt.AddSeconds(5));
        Assert.Equal(6, payment.Version);
    }

    [Fact]
    public void InvalidTransitionDoesNotChangeVersion()
    {
        var payment = PaymentTestData.CreateReceived();

        Assert.Throws<PaymentDomainException>(
            () => payment.StartProcessing(
                PaymentTestData.NodeA,
                1,
                PaymentTestData.CreatedAt.AddSeconds(1)));

        Assert.Equal(1, payment.Version);
    }

    [Fact]
    public void TakeoverAndSecondAttemptRemainMonotonic()
    {
        var payment = PaymentTestData.CreateProcessing();
        var takeoverAt = PaymentTestData.CreatedAt.AddMinutes(1);
        var beforeTakeover = payment.Version;

        payment.Claim(
            PaymentTestData.NodeB,
            2,
            takeoverAt.AddMinutes(1),
            takeoverAt);
        Assert.Equal(beforeTakeover + 1, payment.Version);

        payment.StartProcessing(
            PaymentTestData.NodeB,
            2,
            takeoverAt.AddSeconds(1));
        Assert.Equal(beforeTakeover + 2, payment.Version);
        Assert.Equal(2, payment.Attempt);
    }
}
