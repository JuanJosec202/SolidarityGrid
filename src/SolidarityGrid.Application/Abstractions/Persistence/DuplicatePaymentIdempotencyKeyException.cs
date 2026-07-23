using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Abstractions.Persistence;

public sealed class DuplicatePaymentIdempotencyKeyException
    : PaymentPersistenceException
{
    public DuplicatePaymentIdempotencyKeyException(
        IdempotencyKey idempotencyKey,
        Exception innerException)
        : base("A payment with the same idempotency key already exists.", innerException)
    {
        IdempotencyKey = idempotencyKey;
    }

    public IdempotencyKey IdempotencyKey { get; }
}
