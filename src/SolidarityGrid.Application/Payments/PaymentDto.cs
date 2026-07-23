namespace SolidarityGrid.Application.Payments;

public sealed record PaymentDto(
    Guid Id,
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
    DateTimeOffset? CompletedAtUtc);
