using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed record ReceivePaymentClaimResult(
    bool Granted,
    bool AlreadyApplied,
    PaymentStatus? CurrentStatus,
    NodeId? CurrentOwnerNodeId,
    long CurrentTerm,
    DateTimeOffset? LeaseExpiresAtUtc,
    string? ErrorCode)
{
    public static ReceivePaymentClaimResult FromPayment(
        Payment payment,
        bool granted,
        bool alreadyApplied,
        string? errorCode = null) =>
        new(
            granted,
            alreadyApplied,
            payment.Status,
            payment.OwnerNodeId,
            payment.Term,
            payment.LeaseExpiresAtUtc,
            errorCode);

    public static ReceivePaymentClaimResult NotFound() =>
        new(false, false, null, null, 0, null, PaymentCoordinationErrorCodes.Failed);
}
