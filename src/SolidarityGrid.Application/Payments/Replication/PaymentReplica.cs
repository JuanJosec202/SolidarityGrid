using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Replication;

public sealed record PaymentReplica(
    Guid PaymentId,
    IdempotencyKey IdempotencyKey,
    Money Amount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ReplicatedAtUtc);
