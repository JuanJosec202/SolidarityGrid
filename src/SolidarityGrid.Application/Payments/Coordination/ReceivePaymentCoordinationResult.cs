using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed record ReceivePaymentCoordinationResult(
    bool Applied,
    bool AlreadyApplied,
    PaymentStatus? CurrentStatus,
    NodeId? CurrentOwnerNodeId,
    long CurrentTerm,
    DateTimeOffset? LeaseExpiresAtUtc,
    string? ErrorCode)
{
    public static ReceivePaymentCoordinationResult FromPayment(
        Payment payment,
        bool applied,
        bool alreadyApplied,
        string? errorCode = null) =>
        new(
            applied,
            alreadyApplied,
            payment.Status,
            payment.OwnerNodeId,
            payment.Term,
            payment.LeaseExpiresAtUtc,
            errorCode);

    public static ReceivePaymentCoordinationResult NotFound() =>
        new(false, false, null, null, 0, null, PaymentCoordinationErrorCodes.Failed);
}
