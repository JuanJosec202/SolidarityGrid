using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed record PaymentClaimRequest(
    Guid PaymentId,
    NodeId CandidateNodeId,
    long ProposedTerm,
    DateTimeOffset LeaseExpiresAtUtc,
    DateTimeOffset OccurredAtUtc);
