namespace SolidarityGrid.Application.Payments;

public static class PaymentApplicationErrorCodes
{
    public const string IdempotencyKeyPayloadConflict =
        "IDEMPOTENCY_KEY_PAYLOAD_CONFLICT";
    public const string PaymentInvalidRequest = "PAYMENT_INVALID_REQUEST";
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
}
