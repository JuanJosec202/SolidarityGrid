using SolidarityGrid.Infrastructure.Mesh.Configuration;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh;

public sealed class MeshTransportOptionsTests
{
    private readonly MeshTransportOptionsValidator _validator = new();

    [Fact]
    public void PositiveTimeoutWithinLimitIsValid()
    {
        var result = _validator.Validate(
            null,
            new MeshTransportOptions { ProbeTimeoutMilliseconds = 1_000 });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void TimeoutOutsideAllowedRangeIsInvalid(int timeoutMilliseconds)
    {
        var result = _validator.Validate(
            null,
            new MeshTransportOptions
            {
                ProbeTimeoutMilliseconds = timeoutMilliseconds,
            });

        Assert.True(result.Failed);
    }
}
