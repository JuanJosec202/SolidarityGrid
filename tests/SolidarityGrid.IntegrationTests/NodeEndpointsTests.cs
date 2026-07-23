using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SolidarityGrid.Node.Diagnostics;
using Xunit;

namespace SolidarityGrid.IntegrationTests;

public sealed class NodeEndpointsTests : IClassFixture<ValidNodeFactory>
{
    private readonly ValidNodeFactory _factory;

    public NodeEndpointsTests(ValidNodeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RootReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LiveHealthReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyHealthReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NodeReturnsConfiguredIdentity()
    {
        using var client = _factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonDocument>("/node");
        var root = Assert.IsType<JsonDocument>(document).RootElement;

        Assert.Equal("test-node", root.GetProperty("nodeId").GetString());
        Assert.Equal("http://localhost:5999", root.GetProperty("publicUrl").GetString());
        Assert.Equal(2, root.GetProperty("peers").GetArrayLength());
    }

    [Fact]
    public async Task SuppliedCorrelationIdIsPreserved()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "known-correlation-id");

        var response = await client.SendAsync(request);

        Assert.Equal(
            "known-correlation-id",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task MissingCorrelationIdIsGenerated()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        var correlationId = response.Headers
            .GetValues(CorrelationIdMiddleware.HeaderName)
            .Single();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public void InvalidConfigurationPreventsHostStartup()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Node:NodeId"] = string.Empty,
                    });
                });
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("NodeId", exception.ToString(), StringComparison.Ordinal);
    }
}
