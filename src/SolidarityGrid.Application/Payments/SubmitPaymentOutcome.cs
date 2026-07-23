namespace SolidarityGrid.Application.Payments;

public enum SubmitPaymentOutcome
{
    Created,
    Replayed,
    Conflict,
    Invalid,
}
