namespace SolidarityGrid.Domain.Payments.Events;

public sealed record PaymentClaimedDomainEvent(
    Guid PaymentId,
    DateTimeOffset OccurredAtUtc,
    long Version,
    NodeId OwnerNodeId,
    long Term,
    DateTimeOffset LeaseExpiresAtUtc,
    bool IsTakeover) : IDomainEvent;
