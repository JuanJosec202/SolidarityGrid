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

    public async ValueTask AddAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        await dbContext.Payments.AddAsync(payment, cancellationToken);
    }
}
