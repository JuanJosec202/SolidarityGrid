using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed record PaymentClaimPeerResult(
    NodeId PeerNodeId,
    bool Granted,
    bool AlreadyApplied,
    long CurrentTerm,
    NodeId? CurrentOwnerNodeId,
    DateTimeOffset? LeaseExpiresAtUtc,
    string? ErrorCode);
