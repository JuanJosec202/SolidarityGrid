using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments;

public static class PaymentMapper
{
    public static PaymentDto ToDto(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentDto(
            payment.Id,
            payment.IdempotencyKey.Value,
            payment.Amount.Amount,
            payment.Amount.Currency,
            ToStatusToken(payment.Status),
            payment.OwnerNodeId?.Value,
            payment.Term,
            payment.LeaseExpiresAtUtc,
            payment.Attempt,
            payment.Version,
            payment.CreatedAtUtc,
            payment.UpdatedAtUtc,
            payment.CompletedAtUtc);
    }

    private static string ToStatusToken(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Received => "Received",
            PaymentStatus.Replicated => "Replicated",
            PaymentStatus.Claimed => "Claimed",
            PaymentStatus.Processing => "Processing",
            PaymentStatus.Completed => "Completed",
            _ => throw new InvalidOperationException(
                $"Unknown payment status '{status}'."),
        };
}
