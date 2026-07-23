using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Replication;

public sealed record PaymentReplicaResult(
    NodeId PeerNodeId,
    bool Stored,
    bool AlreadyExisted,
    string? ErrorCode,
    string? ErrorMessage);
