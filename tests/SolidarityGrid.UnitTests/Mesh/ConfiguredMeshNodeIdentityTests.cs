using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Node.Configuration;
using SolidarityGrid.Node.Mesh;
using Xunit;

namespace SolidarityGrid.UnitTests.Mesh;

public sealed class ConfiguredMeshNodeIdentityTests
{
    [Fact]
    public void UsesConfiguredNodeIdAndGeneratedInstanceId()
    {
        var expectedInstanceId = Guid.Parse("6f6546ac-2e5f-4ffc-a098-ed4302647fbe");
        var identity = CreateIdentity(expectedInstanceId);

        Assert.Equal("node-a", identity.NodeId.Value);
        Assert.Equal(expectedInstanceId, identity.InstanceId);
        Assert.NotEqual(Guid.Empty, identity.InstanceId);
    }

    [Fact]
    public void InstanceIdIsStableForIdentityLifetime()
    {
        var identity = CreateIdentity(Guid.NewGuid());

        var first = identity.InstanceId;
        var second = identity.InstanceId;

        Assert.Equal(first, second);
    }

    [Fact]
    public void SimulatedProcessesReceiveDifferentInstanceIds()
    {
        var first = CreateIdentity(Guid.NewGuid());
        var second = CreateIdentity(Guid.NewGuid());

        Assert.NotEqual(first.InstanceId, second.InstanceId);
    }

    [Fact]
    public void EmptyGeneratedInstanceIdIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateIdentity(Guid.Empty));

        Assert.Contains("InstanceId", exception.Message, StringComparison.Ordinal);
    }

    private static ConfiguredMeshNodeIdentity CreateIdentity(Guid instanceId) =>
        new(
            Options.Create(new NodeOptions
            {
                NodeId = "node-a",
                PublicUrl = "http://localhost:5101",
                InternalUrl = "http://node-a:8081",
                Environment = "Test",
            }),
            new StubIdGenerator(instanceId));

    private sealed class StubIdGenerator(Guid value) : IIdGenerator
    {
        public Guid NewId() => value;
    }
}
