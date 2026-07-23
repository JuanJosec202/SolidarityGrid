namespace SolidarityGrid.Domain.Payments;

public enum PaymentStatus
{
    Received,
    Replicated,
    Claimed,
    Processing,
    Completed,
}
