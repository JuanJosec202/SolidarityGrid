namespace SolidarityGrid.Domain.Payments.Events;

public sealed record PaymentReplicatedDomainEvent(
    Guid PaymentId,
    DateTimeOffset OccurredAtUtc,
    long Version) : IDomainEvent;
