using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentQueriesTests
{
    [Fact]
    public void MatchingKeyAndAmountMatchRequest()
    {
        var payment = PaymentTestData.CreateReceived();

        var matches = payment.MatchesRequest(
            new IdempotencyKey("PAY-2026-0001"),
            new Money(150_000m, "cop"));

        Assert.True(matches);
    }

    [Fact]
    public void DifferentKeyDoesNotMatchRequest()
    {
        var payment = PaymentTestData.CreateReceived();

        Assert.False(payment.MatchesRequest(
            new IdempotencyKey("different"),
            new Money(150_000m, "COP")));
    }

    [Fact]
    public void DifferentAmountDoesNotMatchRequest()
    {
        var payment = PaymentTestData.CreateReceived();

        Assert.False(payment.MatchesRequest(
            new IdempotencyKey("PAY-2026-0001"),
            new Money(1m, "COP")));
    }

    [Fact]
    public void UnownedStatesCannotBeTakenOver()
    {
        var received = PaymentTestData.CreateReceived();
        var replicated = PaymentTestData.CreateReplicated();
        var future = PaymentTestData.CreatedAt.AddDays(1);

        Assert.False(received.CanBeTakenOver(future));
        Assert.False(replicated.CanBeTakenOver(future));
        Assert.False(received.IsLeaseExpired(future));
        Assert.False(replicated.IsLeaseExpired(future));
    }
}
