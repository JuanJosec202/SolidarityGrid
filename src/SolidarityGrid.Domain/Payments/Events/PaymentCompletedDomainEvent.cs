namespace SolidarityGrid.Domain.Payments.Events;

public sealed record PaymentCompletedDomainEvent(
    Guid PaymentId,
    DateTimeOffset OccurredAtUtc,
    long Version,
    NodeId OwnerNodeId,
    long Term,
    int Attempt) : IDomainEvent;
