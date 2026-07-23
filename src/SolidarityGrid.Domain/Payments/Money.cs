using System.Globalization;

namespace SolidarityGrid.Domain.Payments;

public sealed record Money
{
    public Money(decimal amount, string? currency)
    {
        if (amount <= 0)
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.PaymentAmountMustBePositive,
                "The payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency) ||
            currency.Length != 3 ||
            currency.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.PaymentCurrencyInvalid,
                "The payment currency must contain exactly three letters.");
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");
}
