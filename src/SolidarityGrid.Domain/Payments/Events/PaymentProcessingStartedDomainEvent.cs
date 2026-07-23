namespace SolidarityGrid.Domain.Payments.Events;

public sealed record PaymentProcessingStartedDomainEvent(
    Guid PaymentId,
    DateTimeOffset OccurredAtUtc,
    long Version,
    NodeId OwnerNodeId,
    long Term,
    int Attempt) : IDomainEvent;
