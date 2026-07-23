namespace SolidarityGrid.Application.Abstractions.Persistence;

public sealed class PaymentConcurrencyException : PaymentPersistenceException
{
    public PaymentConcurrencyException(
        Guid paymentId,
        long? expectedVersion,
        long? actualVersion,
        Exception innerException)
        : base("The payment was changed by another operation.", innerException)
    {
        PaymentId = paymentId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public Guid PaymentId { get; }

    public long? ExpectedVersion { get; }

    public long? ActualVersion { get; }
}
