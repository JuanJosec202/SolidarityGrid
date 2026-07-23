using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.UnitTests.Payments;

public sealed class NodeIdTests
{
    [Fact]
    public void ValidNodeIdIsAccepted()
    {
        Assert.Equal("node-a", new NodeId("node-a").Value);
    }

    [Fact]
    public void ValueIsNormalizedToLowercase()
    {
        Assert.Equal("worker-01", new NodeId("WORKER-01").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Node A")]
    [InlineData("node_a")]
    [InlineData("node.a")]
    [InlineData(null)]
    public void InvalidNodeIdIsRejected(string? value)
    {
        var exception = Assert.Throws<PaymentDomainException>(() => new NodeId(value));

        Assert.Equal(PaymentErrorCodes.NodeIdInvalid, exception.Code);
    }

    [Fact]
    public void ValueLongerThan64CharactersIsRejected()
    {
        var exception = Assert.Throws<PaymentDomainException>(
            () => new NodeId(new string('a', 65)));

        Assert.Equal(PaymentErrorCodes.NodeIdInvalid, exception.Code);
    }

    [Fact]
    public void EqualNormalizedValuesHaveValueEquality()
    {
        Assert.Equal(new NodeId("NODE-A"), new NodeId("node-a"));
    }
}
