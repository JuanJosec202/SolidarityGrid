using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed record PaymentCoordinationPeerResult(
    NodeId PeerNodeId,
    bool Applied,
    bool AlreadyApplied,
    string? ErrorCode);
