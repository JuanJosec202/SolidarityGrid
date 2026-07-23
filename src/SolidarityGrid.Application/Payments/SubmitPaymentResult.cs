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
            outcome is SubmitPaymentOutcome.Created or
                SubmitPaymentOutcome.Replayed or
                SubmitPaymentOutcome.ReplicationUnavailable;
        if (requiresPayment != (payment is not null))
        {
            throw new ArgumentException(
                "Successful and replication unavailable results require one payment.",
                nameof(payment));
        }

        var requiresError =
            outcome is SubmitPaymentOutcome.Conflict or
                SubmitPaymentOutcome.Invalid or
                SubmitPaymentOutcome.ReplicationUnavailable;
        if (requiresError != !string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "Error outcomes require exactly one error code.",
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

    public static SubmitPaymentResult ReplicationUnavailable(PaymentDto payment) =>
        new(
            SubmitPaymentOutcome.ReplicationUnavailable,
            payment,
            Replication.PaymentReplicaErrorCodes.QuorumUnavailable,
            "A durable payment quorum is not currently available.");

    public static SubmitPaymentResult Conflict(string message) =>
        new(
            SubmitPaymentOutcome.Conflict,
            null,
            PaymentApplicationErrorCodes.IdempotencyKeyPayloadConflict,
            message);

    public static SubmitPaymentResult Invalid(string code, string message) =>
        new(SubmitPaymentOutcome.Invalid, null, code, message);
}
