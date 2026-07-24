using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Application.Payments.Coordination;

internal static class PaymentCoordinationErrorMapper
{
    public static string ForClaim(PaymentDomainException exception) =>
        exception.Code switch
        {
            PaymentErrorCodes.PaymentLeaseActive =>
                PaymentCoordinationErrorCodes.ClaimLeaseActive,
            PaymentErrorCodes.PaymentTermStale =>
                PaymentCoordinationErrorCodes.ClaimTermStale,
            PaymentErrorCodes.PaymentInvalidState or
            PaymentErrorCodes.PaymentNotReplicated or
            PaymentErrorCodes.PaymentAlreadyCompleted =>
                PaymentCoordinationErrorCodes.ClaimInvalidState,
            _ => PaymentCoordinationErrorCodes.Failed,
        };

    public static string ForMutation(PaymentDomainException exception) =>
        exception.Code switch
        {
            PaymentErrorCodes.PaymentOwnerMismatch =>
                PaymentCoordinationErrorCodes.OwnerMismatch,
            PaymentErrorCodes.PaymentTermMismatch =>
                PaymentCoordinationErrorCodes.TermMismatch,
            PaymentErrorCodes.PaymentLeaseExpired =>
                PaymentCoordinationErrorCodes.LeaseExpired,
            _ => PaymentCoordinationErrorCodes.Failed,
        };
}
