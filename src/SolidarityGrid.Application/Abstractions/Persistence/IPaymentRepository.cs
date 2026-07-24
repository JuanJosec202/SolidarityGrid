using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Abstractions.Persistence;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<Payment?> GetByIdempotencyKeyAsync(
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
        int limit,
        CancellationToken cancellationToken);

    ValueTask AddAsync(
        Payment payment,
        CancellationToken cancellationToken);
}
