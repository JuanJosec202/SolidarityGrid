namespace SolidarityGrid.Domain.Payments.Events;

public sealed record PaymentLeaseRenewedDomainEvent(
    Guid PaymentId,
    DateTimeOffset OccurredAtUtc,
    long Version,
    NodeId OwnerNodeId,
    long Term,
    DateTimeOffset LeaseExpiresAtUtc) : IDomainEvent;
