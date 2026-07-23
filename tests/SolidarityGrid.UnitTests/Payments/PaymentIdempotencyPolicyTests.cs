using SolidarityGrid.Application.Payments;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class PaymentIdempotencyPolicyTests
{
    [Fact]
    public void MissingExistingPaymentCreatesNew()
    {
        var decision = PaymentIdempotencyPolicy.Evaluate(null, CreateCommand());

        Assert.Equal(PaymentIdempotencyDecision.CreateNew, decision);
    }

    [Fact]
    public void MatchingRequestReplaysExistingPayment()
    {
        var payment = PaymentTestData.CreateReceived();

        var decision = PaymentIdempotencyPolicy.Evaluate(payment, CreateCommand());

        Assert.Equal(PaymentIdempotencyDecision.ReplayExisting, decision);
    }

    [Fact]
    public void DifferentAmountConflicts()
    {
        var payment = PaymentTestData.CreateReceived();

        var decision = PaymentIdempotencyPolicy.Evaluate(
            payment,
            CreateCommand(amount: 150_001m));

        Assert.Equal(PaymentIdempotencyDecision.Conflict, decision);
    }

    [Fact]
    public void DifferentCurrencyConflicts()
    {
        var payment = PaymentTestData.CreateReceived();

        var decision = PaymentIdempotencyPolicy.Evaluate(
            payment,
            CreateCommand(currency: "USD"));

        Assert.Equal(PaymentIdempotencyDecision.Conflict, decision);
    }

    [Fact]
    public void IdempotencyKeyComparisonIsCaseSensitive()
    {
        var payment = PaymentTestData.CreateReceived();

        var decision = PaymentIdempotencyPolicy.Evaluate(
            payment,
            CreateCommand(idempotencyKey: "pay-2026-0001"));

        Assert.Equal(PaymentIdempotencyDecision.Conflict, decision);
    }

    [Fact]
    public void CurrencyNormalizationIsReused()
    {
        var payment = PaymentTestData.CreateReceived(
            amount: new Money(25.50m, "USD"));

        var decision = PaymentIdempotencyPolicy.Evaluate(
            payment,
            CreateCommand(amount: 25.50m, currency: "usd"));

        Assert.Equal(PaymentIdempotencyDecision.ReplayExisting, decision);
    }

    [Fact]
    public void EvaluationDoesNotModifyPaymentOrGenerateEvents()
    {
        var payment = PaymentTestData.CreateReceived();
        payment.DequeueDomainEvents();
        var originalVersion = payment.Version;
        var originalStatus = payment.Status;

        PaymentIdempotencyPolicy.Evaluate(payment, CreateCommand());

        Assert.Equal(originalVersion, payment.Version);
        Assert.Equal(originalStatus, payment.Status);
        Assert.Empty(payment.DequeueDomainEvents());
    }

    [Fact]
    public void KeyTrimmingIsReused()
    {
        var payment = PaymentTestData.CreateReceived();

        var decision = PaymentIdempotencyPolicy.Evaluate(
            payment,
            CreateCommand(idempotencyKey: "  PAY-2026-0001  "));

        Assert.Equal(PaymentIdempotencyDecision.ReplayExisting, decision);
    }

    private static SubmitPaymentCommand CreateCommand(
        string idempotencyKey = "PAY-2026-0001",
        decimal amount = 150_000m,
        string currency = "COP") =>
        new(idempotencyKey, amount, currency);
}
