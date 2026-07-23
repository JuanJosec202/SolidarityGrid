namespace SolidarityGrid.Application.Payments;

public sealed class SubmitPaymentResult
{
    private SubmitPaymentResult(
        SubmitPaymentOutcome outcome,
        PaymentDto? payment,
        string? errorCode,
        string? errorMessage)
    {
        var requiresPayment =
            outcome is SubmitPaymentOutcome.Created or SubmitPaymentOutcome.Replayed;
        if (requiresPayment != (payment is not null))
        {
            throw new ArgumentException(
                "Created and replayed results require exactly one payment.",
                nameof(payment));
        }

        var requiresError =
            outcome is SubmitPaymentOutcome.Conflict or SubmitPaymentOutcome.Invalid;
        if (requiresError != !string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "Conflict and invalid results require exactly one error code.",
                nameof(errorCode));
        }

        Outcome = outcome;
        Payment = payment;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public SubmitPaymentOutcome Outcome { get; }

    public PaymentDto? Payment { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static SubmitPaymentResult Created(PaymentDto payment) =>
        new(SubmitPaymentOutcome.Created, payment, null, null);

    public static SubmitPaymentResult Replayed(PaymentDto payment) =>
        new(SubmitPaymentOutcome.Replayed, payment, null, null);

    public static SubmitPaymentResult Conflict(string message) =>
        new(
            SubmitPaymentOutcome.Conflict,
            null,
            PaymentApplicationErrorCodes.IdempotencyKeyPayloadConflict,
            message);

    public static SubmitPaymentResult Invalid(string code, string message) =>
        new(SubmitPaymentOutcome.Invalid, null, code, message);
}
