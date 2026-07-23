using SolidarityGrid.Infrastructure.Mesh.Configuration;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh.Health;

public sealed class MeshFailureDetectorOptionsTests
{
    private readonly MeshFailureDetectorOptionsValidator _validator = new();

    [Fact]
    public void OrderedIntervalsAreValid()
    {
        Assert.True(_validator.Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData(0, 3000, 5000)]
    [InlineData(1000, 1000, 5000)]
    [InlineData(1000, 5000, 5000)]
    [InlineData(1000, 3000, 60001)]
    public void InvalidIntervalsFail(int heartbeat, int suspect, int unreachable)
    {
        var result = _validator.Validate(
            null,
            new MeshFailureDetectorOptions
            {
                HeartbeatIntervalMilliseconds = heartbeat,
                SuspectAfterMilliseconds = suspect,
                UnreachableAfterMilliseconds = unreachable,
            });

        Assert.True(result.Failed);
    }

    private static MeshFailureDetectorOptions Valid() =>
        new()
        {
            HeartbeatIntervalMilliseconds = 1000,
            SuspectAfterMilliseconds = 3000,
            UnreachableAfterMilliseconds = 5000,
        };
}
