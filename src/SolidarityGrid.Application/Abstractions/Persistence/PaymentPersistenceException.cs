namespace SolidarityGrid.Application.Abstractions.Persistence;

public class PaymentPersistenceException : Exception
{
    public PaymentPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
