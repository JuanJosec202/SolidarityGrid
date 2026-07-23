using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.UnitTests.Payments;

internal static class PaymentTestData
{
    public static readonly Guid PaymentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    public static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    public static readonly NodeId NodeA = new("node-a");
    public static readonly NodeId NodeB = new("node-b");

    public static Payment CreateReceived(
        IdempotencyKey? idempotencyKey = null,
        Money? amount = null,
        DateTimeOffset? createdAt = null) =>
        Payment.Create(
            PaymentId,
            idempotencyKey ?? new IdempotencyKey("PAY-2026-0001"),
            amount ?? new Money(150_000m, "COP"),
            createdAt ?? CreatedAt);

    public static Payment CreateReplicated()
    {
        var payment = CreateReceived();
        payment.MarkReplicated(CreatedAt.AddSeconds(1));
        return payment;
    }

    public static Payment CreateClaimed(
        NodeId? owner = null,
        long term = 1,
        DateTimeOffset? leaseExpiresAt = null)
    {
        var payment = CreateReplicated();
        payment.Claim(
            owner ?? NodeA,
            term,
            leaseExpiresAt ?? CreatedAt.AddMinutes(1),
            CreatedAt.AddSeconds(2));
        return payment;
    }

    public static Payment CreateProcessing(
        NodeId? owner = null,
        long term = 1,
        DateTimeOffset? leaseExpiresAt = null)
    {
        var actualOwner = owner ?? NodeA;
        var payment = CreateClaimed(actualOwner, term, leaseExpiresAt);
        payment.StartProcessing(actualOwner, term, CreatedAt.AddSeconds(3));
        return payment;
    }
}
