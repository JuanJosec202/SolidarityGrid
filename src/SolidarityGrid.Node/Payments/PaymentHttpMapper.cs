using SolidarityGrid.Application.Payments;

namespace SolidarityGrid.Node.Payments;

public static class PaymentHttpMapper
{
    public static PaymentResponse ToResponse(PaymentDto payment) =>
        new(
            payment.Id,
            payment.IdempotencyKey,
            payment.Amount,
            payment.Currency,
            payment.Status,
            payment.OwnerNodeId,
            payment.Term,
            payment.LeaseExpiresAtUtc,
            payment.Attempt,
            payment.Version,
            payment.CreatedAtUtc,
            payment.UpdatedAtUtc,
            payment.CompletedAtUtc);

    public static SubmitPaymentResponse ToSubmitResponse(
        PaymentDto payment,
        string acceptedByNodeId,
        bool isReplay) =>
        new(
            payment.Id,
            payment.IdempotencyKey,
            payment.Amount,
            payment.Currency,
            payment.Status,
            payment.OwnerNodeId,
            payment.Term,
            payment.LeaseExpiresAtUtc,
            payment.Attempt,
            payment.Version,
            payment.CreatedAtUtc,
            payment.UpdatedAtUtc,
            payment.CompletedAtUtc,
            acceptedByNodeId,
            isReplay);
}
