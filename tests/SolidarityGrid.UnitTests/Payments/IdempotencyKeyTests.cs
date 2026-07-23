using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class IdempotencyKeyTests
{
    [Fact]
    public void ValidValueIsPreserved()
    {
        var key = new IdempotencyKey("PAY-2026-0001");

        Assert.Equal("PAY-2026-0001", key.Value);
        Assert.Equal("PAY-2026-0001", key.ToString());
    }

    [Fact]
    public void OuterWhitespaceIsTrimmed()
    {
        var key = new IdempotencyKey("  customer-88-order-42  ");

        Assert.Equal("customer-88-order-42", key.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingValueIsRejected(string? value)
    {
        var exception = Assert.Throws<PaymentDomainException>(() => new IdempotencyKey(value));

        Assert.Equal(PaymentErrorCodes.IdempotencyKeyRequired, exception.Code);
    }

    [Fact]
    public void ValueWith128CharactersIsAccepted()
    {
        var value = new string('a', 128);

        var key = new IdempotencyKey(value);

        Assert.Equal(value, key.Value);
    }

    [Fact]
    public void ValueWith129CharactersIsRejected()
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => new IdempotencyKey(new string('a', 129)));

        Assert.Equal(PaymentErrorCodes.IdempotencyKeyTooLong, exception.Code);
    }

    [Fact]
    public void EqualValuesHaveValueEquality()
    {
        Assert.Equal(new IdempotencyKey("same-key"), new IdempotencyKey("same-key"));
    }

    [Fact]
    public void EqualityIsCaseSensitive()
    {
        Assert.NotEqual(new IdempotencyKey("PAY-1"), new IdempotencyKey("pay-1"));
    }
}
