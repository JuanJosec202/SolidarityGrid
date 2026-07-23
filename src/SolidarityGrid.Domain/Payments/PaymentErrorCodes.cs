namespace SolidarityGrid.Domain.Payments;

public static class PaymentErrorCodes
{
    public const string PaymentIdRequired = "PAYMENT_ID_REQUIRED";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyTooLong = "IDEMPOTENCY_KEY_TOO_LONG";
    public const string PaymentAmountRequired = "PAYMENT_AMOUNT_REQUIRED";
    public const string PaymentAmountMustBePositive = "PAYMENT_AMOUNT_MUST_BE_POSITIVE";
    public const string PaymentCurrencyInvalid = "PAYMENT_CURRENCY_INVALID";
    public const string NodeIdInvalid = "NODE_ID_INVALID";
    public const string PaymentNotReplicated = "PAYMENT_NOT_REPLICATED";
    public const string PaymentAlreadyCompleted = "PAYMENT_ALREADY_COMPLETED";
    public const string PaymentLeaseActive = "PAYMENT_LEASE_ACTIVE";
    public const string PaymentLeaseExpired = "PAYMENT_LEASE_EXPIRED";
    public const string PaymentLeaseInvalid = "PAYMENT_LEASE_INVALID";
    public const string PaymentLeaseMustIncrease = "PAYMENT_LEASE_MUST_INCREASE";
    public const string PaymentTermStale = "PAYMENT_TERM_STALE";
    public const string PaymentOwnerMismatch = "PAYMENT_OWNER_MISMATCH";
    public const string PaymentTermMismatch = "PAYMENT_TERM_MISMATCH";
    public const string PaymentInvalidState = "PAYMENT_INVALID_STATE";
}
