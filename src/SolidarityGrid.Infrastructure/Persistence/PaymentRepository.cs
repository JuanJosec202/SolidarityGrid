using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Infrastructure.Persistence;

public sealed class PaymentRepository(SolidarityGridDbContext dbContext)
    : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken) =>
        dbContext.Payments.SingleOrDefaultAsync(
            payment => payment.Id == paymentId,
            cancellationToken);

    public Task<Payment?> GetByIdempotencyKeyAsync(
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        return dbContext.Payments.SingleOrDefaultAsync(
            payment => payment.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<Payment>> GetReplicatedPaymentsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await dbContext.Payments
            .Where(payment => payment.Status == PaymentStatus.Replicated)
            .OrderBy(payment => payment.CreatedAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Payment>> GetRecoverablePaymentsAsync(
        DateTimeOffset utcNow,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var normalizedUtcNow = utcNow.ToUniversalTime();

        return await dbContext.Payments
            .Where(payment =>
                (payment.Status == PaymentStatus.Claimed ||
                 payment.Status == PaymentStatus.Processing) &&
                payment.LeaseExpiresAtUtc != null &&
                payment.LeaseExpiresAtUtc <= normalizedUtcNow)
            .OrderBy(payment => payment.LeaseExpiresAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask AddAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        await dbContext.Payments.AddAsync(payment, cancellationToken);
    }
}
