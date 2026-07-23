using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.IntegrationTests.Persistence;

internal static class PaymentPersistenceTestData
{
    public static readonly DateTimeOffset CreatedAt =
        new(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);

    public static Payment Create(
        string key = "PAY-1",
        decimal amount = 25.50m,
        DateTimeOffset? createdAt = null) =>
        Payment.Create(
            Guid.NewGuid(),
            new IdempotencyKey(key),
            new Money(amount, "usd"),
            createdAt ?? CreatedAt);

    public static Payment CreateProcessing(string key = "PAY-PROCESSING")
    {
        var payment = Create(key);
        payment.MarkReplicated(CreatedAt.AddSeconds(1));
        payment.Claim(
            new NodeId("node-a"),
            proposedTerm: 1,
            leaseExpiresAtUtc: CreatedAt.AddMinutes(10),
            occurredAtUtc: CreatedAt.AddSeconds(2));
        payment.StartProcessing(
            new NodeId("node-a"),
            term: 1,
            occurredAtUtc: CreatedAt.AddSeconds(3));
        return payment;
    }
}
