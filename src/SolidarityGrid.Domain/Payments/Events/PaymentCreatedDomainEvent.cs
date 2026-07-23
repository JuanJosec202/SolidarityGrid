namespace SolidarityGrid.Domain.Payments.Events;

public sealed record PaymentCreatedDomainEvent(
    Guid PaymentId,
    DateTimeOffset OccurredAtUtc,
    long Version,
    IdempotencyKey IdempotencyKey,
    Money Amount) : IDomainEvent;
