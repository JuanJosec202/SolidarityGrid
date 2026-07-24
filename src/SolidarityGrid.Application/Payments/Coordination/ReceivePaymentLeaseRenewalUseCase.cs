using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed class ReceivePaymentLeaseRenewalUseCase(
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ReceivePaymentCoordinationResult> ExecuteAsync(
        PaymentCoordinationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var payment = await paymentRepository.GetByIdAsync(
            command.PaymentId,
            cancellationToken);
        if (payment is null)
        {
            return ReceivePaymentCoordinationResult.NotFound();
        }

        if (command.LeaseExpiresAtUtc is not { } leaseExpiresAtUtc)
        {
            return ReceivePaymentCoordinationResult.FromPayment(
                payment,
                applied: false,
                alreadyApplied: false,
                PaymentCoordinationErrorCodes.Failed);
        }

        try
        {
            var changed = payment.RenewLease(
                command.OwnerNodeId,
                command.Term,
                leaseExpiresAtUtc,
                command.OccurredAtUtc);
            if (changed)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return ReceivePaymentCoordinationResult.FromPayment(
                payment,
                applied: true,
                alreadyApplied: !changed);
        }
        catch (PaymentDomainException exception)
        {
            return ReceivePaymentCoordinationResult.FromPayment(
                payment,
                applied: false,
                alreadyApplied: false,
                PaymentCoordinationErrorMapper.ForMutation(exception));
        }
    }
}
