using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class MoneyTests
{
    [Fact]
    public void CopAmountIsValid()
    {
        var money = new Money(150_000m, "COP");

        Assert.Equal(150_000m, money.Amount);
        Assert.Equal("COP", money.Currency);
    }

    [Fact]
    public void UsdAmountIsValid()
    {
        var money = new Money(25.50m, "USD");

        Assert.Equal(25.50m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAmountIsRejected(int amount)
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => new Money(amount, "COP"));

        Assert.Equal(PaymentErrorCodes.PaymentAmountMustBePositive, exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U5D")]
    [InlineData(null)]
    public void InvalidCurrencyIsRejected(string? currency)
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => new Money(10m, currency));

        Assert.Equal(PaymentErrorCodes.PaymentCurrencyInvalid, exception.Code);
    }

    [Fact]
    public void CurrencyIsNormalizedToUppercase()
    {
        Assert.Equal("USD", new Money(10m, "usd").Currency);
    }

    [Fact]
    public void EqualValuesHaveValueEquality()
    {
        Assert.Equal(new Money(10m, "usd"), new Money(10m, "USD"));
    }
}
