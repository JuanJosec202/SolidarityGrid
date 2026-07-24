namespace SolidarityGrid.Application.Payments.Recovery;

public enum PaymentRecoveryDecision
{
    Eligible,
    LeaseStillActive,
    OwnerNotUnreachable,
    OwnerIsLocal,
    InvalidState,
    MissingOwner,
}
