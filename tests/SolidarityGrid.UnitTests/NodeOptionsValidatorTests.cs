using Microsoft.Extensions.Options;
using SolidarityGrid.Node.Configuration;
using Xunit;

namespace SolidarityGrid.UnitTests;

public sealed class NodeOptionsValidatorTests
{
    private readonly NodeOptionsValidator _validator = new();

    [Fact]
    public void ValidConfigurationSucceeds()
    {
        var options = CreateValidOptions();

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void EmptyNodeIdFails()
    {
        var options = new NodeOptions
        {
            NodeId = string.Empty,
            PublicUrl = "http://localhost:5101",
            InternalUrl = "http://node-a:8080",
            Environment = "Test",
        };

        var result = _validator.Validate(Options.DefaultName, options);

        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        Assert.Contains(failures, failure => failure.Contains("NodeId", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidUrlFails()
    {
        var options = new NodeOptions
        {
            NodeId = "node-a",
            PublicUrl = "not-a-url",
            InternalUrl = "http://node-a:8080",
            Environment = "Test",
        };

        var result = _validator.Validate(Options.DefaultName, options);

        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        Assert.Contains(failures, failure => failure.Contains("PublicUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicatePeerFails()
    {
        var options = new NodeOptions
        {
            NodeId = "node-a",
            PublicUrl = "http://localhost:5101",
            InternalUrl = "http://node-a:8080",
            Environment = "Test",
            Peers =
            [
                new PeerOptions { NodeId = "node-b", Url = "http://node-b:8080" },
                new PeerOptions { NodeId = "NODE-B", Url = "http://node-b:8080" },
            ],
        };

        var result = _validator.Validate(Options.DefaultName, options);

        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        Assert.Contains(failures, failure => failure.Contains("duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void LocalNodeAsPeerFails()
    {
        var options = new NodeOptions
        {
            NodeId = "node-a",
            PublicUrl = "http://localhost:5101",
            InternalUrl = "http://node-a:8080",
            Environment = "Test",
            Peers =
            [
                new PeerOptions { NodeId = "node-a", Url = "http://node-a:8080" },
            ],
        };

        var result = _validator.Validate(Options.DefaultName, options);

        var failures = Assert.IsAssignableFrom<IEnumerable<string>>(result.Failures);
        Assert.Contains(failures, failure => failure.Contains("local NodeId", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoDistinctPeersSucceed()
    {
        var options = new NodeOptions
        {
            NodeId = "node-a",
            PublicUrl = "http://localhost:5101",
            InternalUrl = "http://node-a:8080",
            Environment = "Test",
            Peers =
            [
                new PeerOptions { NodeId = "node-b", Url = "http://node-b:8080" },
                new PeerOptions { NodeId = "node-c", Url = "http://node-c:8080" },
            ],
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    private static NodeOptions CreateValidOptions() =>
        new()
        {
            NodeId = "node-a",
            PublicUrl = "http://localhost:5101",
            InternalUrl = "http://node-a:8080",
            Environment = "Test",
        };
}
