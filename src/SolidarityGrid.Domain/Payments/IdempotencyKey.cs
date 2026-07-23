namespace SolidarityGrid.Domain.Payments;

public sealed record IdempotencyKey
{
    public const int MaximumLength = 128;

    public IdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.IdempotencyKeyRequired,
                "The idempotency key is required.");
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > MaximumLength)
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.IdempotencyKeyTooLong,
                $"The idempotency key cannot exceed {MaximumLength} characters.");
        }

        Value = normalizedValue;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
