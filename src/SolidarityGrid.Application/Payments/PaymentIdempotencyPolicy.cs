using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments;

public static class PaymentIdempotencyPolicy
{
    public static PaymentIdempotencyDecision Evaluate(
        Payment? existingPayment,
        SubmitPaymentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var idempotencyKey = new IdempotencyKey(command.IdempotencyKey);
        var amount = new Money(command.Amount, command.Currency);

        if (existingPayment is null)
        {
            return PaymentIdempotencyDecision.CreateNew;
        }

        return existingPayment.MatchesRequest(idempotencyKey, amount)
            ? PaymentIdempotencyDecision.ReplayExisting
            : PaymentIdempotencyDecision.Conflict;
    }
}
