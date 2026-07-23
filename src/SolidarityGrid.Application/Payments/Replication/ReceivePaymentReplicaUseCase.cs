using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Replication;

public sealed class ReceivePaymentReplicaUseCase(
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ReceivePaymentReplicaResult> ExecuteAsync(
        PaymentReplica replica,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replica);
        cancellationToken.ThrowIfCancellationRequested();

        var existingById = await paymentRepository.GetByIdAsync(
            replica.PaymentId,
            cancellationToken);
        if (existingById is not null)
        {
            return await ApplyToExistingAsync(
                existingById,
                replica,
                cancellationToken);
        }

        var existingByKey = await paymentRepository.GetByIdempotencyKeyAsync(
            replica.IdempotencyKey,
            cancellationToken);
        if (existingByKey is not null)
        {
            return ReceivePaymentReplicaResult.Conflict(
                "The idempotency key belongs to a different payment.");
        }

        var payment = Payment.Create(
            replica.PaymentId,
            replica.IdempotencyKey,
            replica.Amount,
            replica.CreatedAtUtc);
        _ = payment.MarkReplicated(replica.ReplicatedAtUtc);
        await paymentRepository.AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReceivePaymentReplicaResult.Success(
            alreadyExisted: false,
            PaymentMapper.ToDto(payment).Status,
            payment.Version);
    }

    private async Task<ReceivePaymentReplicaResult> ApplyToExistingAsync(
        Payment existing,
        PaymentReplica replica,
        CancellationToken cancellationToken)
    {
        if (!Matches(existing, replica))
        {
            return ReceivePaymentReplicaResult.Conflict(
                "The payment replica conflicts with the stored payment.");
        }

        var changed = existing.MarkReplicated(replica.ReplicatedAtUtc);
        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ReceivePaymentReplicaResult.Success(
            alreadyExisted: true,
            PaymentMapper.ToDto(existing).Status,
            existing.Version);
    }

    private static bool Matches(Payment payment, PaymentReplica replica) =>
        payment.Id == replica.PaymentId &&
        payment.IdempotencyKey == replica.IdempotencyKey &&
        payment.Amount == replica.Amount &&
        payment.CreatedAtUtc == replica.CreatedAtUtc.ToUniversalTime();
}
