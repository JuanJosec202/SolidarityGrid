namespace SolidarityGrid.Application.Payments;

public enum SubmitPaymentOutcome
{
    Created,
    Replayed,
    ReplicationUnavailable,
    Conflict,
    Invalid,
}
