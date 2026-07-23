using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Infrastructure.Persistence;

public sealed class SolidarityGridUnitOfWork(SolidarityGridDbContext dbContext)
    : IUnitOfWork
{
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteUniqueConstraintExtendedErrorCode = 2067;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw await CreateConcurrencyExceptionAsync(exception, cancellationToken);
        }
        catch (DbUpdateException exception)
            when (TryGetUniqueConstraintPayment(exception, out var constrainedPayment))
        {
            if (await IsIdempotencyKeyConflictAsync(
                    constrainedPayment,
                    cancellationToken))
            {
                throw new DuplicatePaymentIdempotencyKeyException(
                    constrainedPayment.IdempotencyKey,
                    exception);
            }

            throw;
        }
    }

    private static bool TryGetUniqueConstraintPayment(
        DbUpdateException exception,
        out Payment constrainedPayment)
    {
        constrainedPayment = null!;

        if (exception.InnerException is not SqliteException sqliteException ||
            sqliteException.SqliteErrorCode != SqliteConstraintErrorCode ||
            sqliteException.SqliteExtendedErrorCode != SqliteUniqueConstraintExtendedErrorCode)
        {
            return false;
        }

        var payment = exception.Entries
            .Select(entry => entry.Entity)
            .OfType<Payment>()
            .SingleOrDefault();
        if (payment is null)
        {
            return false;
        }

        constrainedPayment = payment;
        return true;
    }

    private async Task<bool> IsIdempotencyKeyConflictAsync(
        Payment constrainedPayment,
        CancellationToken cancellationToken)
    {
        var matchingTrackedPayments = dbContext.ChangeTracker
            .Entries<Payment>()
            .Count(entry =>
                entry.Entity.IdempotencyKey == constrainedPayment.IdempotencyKey);
        if (matchingTrackedPayments > 1)
        {
            return true;
        }

        return await dbContext.Payments
            .AsNoTracking()
            .AnyAsync(
                payment =>
                    payment.IdempotencyKey == constrainedPayment.IdempotencyKey,
                cancellationToken);
    }

    private static async Task<PaymentConcurrencyException> CreateConcurrencyExceptionAsync(
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken)
    {
        var paymentEntry = exception.Entries.SingleOrDefault(
            entry => entry.Entity is Payment);
        if (paymentEntry?.Entity is not Payment payment)
        {
            return new PaymentConcurrencyException(
                Guid.Empty,
                null,
                null,
                exception);
        }

        var expectedVersion = paymentEntry.OriginalValues.GetValue<long>(
            nameof(Payment.Version));
        var databaseValues = await paymentEntry.GetDatabaseValuesAsync(cancellationToken);
        var actualVersion = databaseValues?.GetValue<long>(nameof(Payment.Version));

        return new PaymentConcurrencyException(
            payment.Id,
            expectedVersion,
            actualVersion,
            exception);
    }
}
