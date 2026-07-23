namespace SolidarityGrid.Application.Payments;

public enum PaymentIdempotencyDecision
{
    CreateNew,
    ReplayExisting,
    Conflict,
}
