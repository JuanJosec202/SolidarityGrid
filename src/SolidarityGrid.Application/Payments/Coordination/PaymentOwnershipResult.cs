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
    int Rounds,
    bool IsTakeover,
    string? PreviousOwnerNodeId,
    long PreviousTerm,
    string? NewOwnerNodeId)
{
    public static PaymentOwnershipResult Acquired(
        long term,
        DateTimeOffset leaseExpiresAtUtc,
        int rounds,
        bool isTakeover,
        string? previousOwnerNodeId,
        long previousTerm,
        string newOwnerNodeId) =>
        new(
            PaymentOwnershipStatus.Acquired,
            term,
            leaseExpiresAtUtc,
            rounds,
            isTakeover,
            previousOwnerNodeId,
            previousTerm,
            newOwnerNodeId);

    public static PaymentOwnershipResult NotAcquired(
        int rounds,
        bool isTakeover = false,
        string? previousOwnerNodeId = null,
        long previousTerm = 0) =>
        new(
            PaymentOwnershipStatus.NotAcquired,
            null,
            null,
            rounds,
            isTakeover,
            previousOwnerNodeId,
            previousTerm,
            null);

    public static PaymentOwnershipResult QuorumUnavailable(
        int rounds,
        bool isTakeover = false,
        string? previousOwnerNodeId = null,
        long previousTerm = 0) =>
        new(
            PaymentOwnershipStatus.QuorumUnavailable,
            null,
            null,
            rounds,
            isTakeover,
            previousOwnerNodeId,
            previousTerm,
            null);
}
