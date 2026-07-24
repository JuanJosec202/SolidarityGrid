namespace SolidarityGrid.Application.Payments.Coordination;

public enum PaymentOwnershipStatus
{
    Acquired,
    NotAcquired,
    QuorumUnavailable,
}

public sealed record PaymentOwnershipResult(
    PaymentOwnershipStatus Status,
    long? Term,
    DateTimeOffset? LeaseExpiresAtUtc,
    int Rounds)
{
    public static PaymentOwnershipResult Acquired(
        long term,
        DateTimeOffset leaseExpiresAtUtc,
        int rounds) =>
        new(PaymentOwnershipStatus.Acquired, term, leaseExpiresAtUtc, rounds);

    public static PaymentOwnershipResult NotAcquired(int rounds) =>
        new(PaymentOwnershipStatus.NotAcquired, null, null, rounds);

    public static PaymentOwnershipResult QuorumUnavailable(int rounds) =>
        new(PaymentOwnershipStatus.QuorumUnavailable, null, null, rounds);
}
