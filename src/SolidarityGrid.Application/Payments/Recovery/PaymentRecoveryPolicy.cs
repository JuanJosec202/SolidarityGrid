using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Recovery;

public static class PaymentRecoveryPolicy
{
    public static PaymentRecoveryDecision Evaluate(
        Payment payment,
        NodeId localNodeId,
        MeshPeerHealthSnapshot? ownerHealth,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(localNodeId);

        if (payment.Status is not (PaymentStatus.Claimed or PaymentStatus.Processing))
        {
            return PaymentRecoveryDecision.InvalidState;
        }

        if (payment.OwnerNodeId is not { } ownerNodeId)
        {
            return PaymentRecoveryDecision.MissingOwner;
        }

        if (ownerNodeId == localNodeId)
        {
            return PaymentRecoveryDecision.OwnerIsLocal;
        }

        if (!payment.IsLeaseExpired(utcNow))
        {
            return PaymentRecoveryDecision.LeaseStillActive;
        }

        return ownerHealth is not null &&
               ownerHealth.PeerNodeId == ownerNodeId &&
               ownerHealth.Status == MeshPeerHealthStatus.Unreachable
            ? PaymentRecoveryDecision.Eligible
            : PaymentRecoveryDecision.OwnerNotUnreachable;
    }
}
