namespace SolidarityGrid.Domain.Payments;

public sealed class PaymentDomainException : Exception
{
    public PaymentDomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
