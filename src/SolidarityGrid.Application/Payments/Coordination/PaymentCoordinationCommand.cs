using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed record PaymentCoordinationCommand(
    Guid PaymentId,
    NodeId OwnerNodeId,
    long Term,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? LeaseExpiresAtUtc = null);
