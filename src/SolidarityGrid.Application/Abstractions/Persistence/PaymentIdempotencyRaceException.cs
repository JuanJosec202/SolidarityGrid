using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Abstractions.Persistence;

public sealed class PaymentIdempotencyRaceException
    : PaymentPersistenceException
{
    public PaymentIdempotencyRaceException(
        IdempotencyKey idempotencyKey,
        Exception innerException)
        : base(
            "The winning payment could not be loaded after an idempotency conflict.",
            innerException)
    {
        IdempotencyKey = idempotencyKey;
    }

    public IdempotencyKey IdempotencyKey { get; }
}
