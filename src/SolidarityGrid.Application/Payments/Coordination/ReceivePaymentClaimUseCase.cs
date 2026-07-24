using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

public sealed class ReceivePaymentClaimUseCase(
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ReceivePaymentClaimResult> ExecuteAsync(
        PaymentClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var payment = await paymentRepository.GetByIdAsync(
            request.PaymentId,
            cancellationToken);
        if (payment is null)
        {
            return ReceivePaymentClaimResult.NotFound();
        }

        try
        {
            var changed = payment.Claim(
                request.CandidateNodeId,
                request.ProposedTerm,
                request.LeaseExpiresAtUtc,
                request.OccurredAtUtc);
            if (changed)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return ReceivePaymentClaimResult.FromPayment(
                payment,
                granted: true,
                alreadyApplied: !changed,
                PaymentCoordinationErrorCodes.ClaimGranted);
        }
        catch (PaymentDomainException exception)
        {
            return ReceivePaymentClaimResult.FromPayment(
                payment,
                granted: false,
                alreadyApplied: false,
                PaymentCoordinationErrorMapper.ForClaim(exception));
        }
    }
}
