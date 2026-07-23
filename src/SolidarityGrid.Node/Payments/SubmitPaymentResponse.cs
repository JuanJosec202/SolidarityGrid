namespace SolidarityGrid.Node.Payments;

public sealed record SubmitPaymentResponse(
    Guid PaymentId,
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string Status,
    string? OwnerNodeId,
    long Term,
    DateTimeOffset? LeaseExpiresAtUtc,
    int Attempt,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string AcceptedByNodeId,
    bool IsReplay);
