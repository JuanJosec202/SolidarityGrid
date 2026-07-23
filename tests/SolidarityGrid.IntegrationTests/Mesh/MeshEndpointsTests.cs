using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Domain.Payments;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Mesh;

public sealed class MeshEndpointsTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task MeshPeersReturnsOneResultPerPeerIncludingFailures()
    {
        using var factory = new MeshEndpointFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mesh/peers");
        request.Headers.Add("X-Correlation-ID", "http-mesh-correlation");

        using var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<MeshResponse>(
            rawBody,
            SerializerOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("test-node", body.NodeId);
        Assert.NotEqual(Guid.Empty, body.InstanceId);
        Assert.Equal(1, body.ProtocolVersion);
        Assert.Equal(2, body.Peers.Length);
        Assert.True(body.Peers.Single(peer => peer.NodeId == "peer-one").IsReachable);
        var failed = body.Peers.Single(peer => peer.NodeId == "peer-two");
        Assert.False(failed.IsReachable);
        Assert.Equal(MeshErrorCodes.PeerUnreachable, failed.ErrorCode);
        Assert.Equal("http-mesh-correlation", factory.Probe.LastCorrelationId);
        Assert.Equal(
            "http-mesh-correlation",
            response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.DoesNotContain("RpcException", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", rawBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MeshPeersUsesGeneratedCorrelationId()
    {
        using var factory = new MeshEndpointFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/mesh/peers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers
            .GetValues("X-Correlation-ID")
            .Single();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.Equal(correlationId, factory.Probe.LastCorrelationId);
    }

    [Fact]
    public async Task HttpCancellationCancelsPendingProbes()
    {
        using var factory = new CancellingMeshEndpointFactory();
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("/mesh/peers", cancellation.Token));

        await factory.Probe.CancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
    }

    private sealed class MeshEndpointFactory : ValidNodeFactory
    {
        public StubMeshPeerProbe Probe { get; } = new();

        protected override void ConfigureWebHost(
            Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMeshPeerProbe>();
                services.AddSingleton<IMeshPeerProbe>(Probe);
            });
        }
    }

    private sealed class CancellingMeshEndpointFactory : ValidNodeFactory
    {
        public BlockingMeshPeerProbe Probe { get; } = new();

        protected override void ConfigureWebHost(
            Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMeshPeerProbe>();
                services.AddSingleton<IMeshPeerProbe>(Probe);
            });
        }
    }

    private sealed class StubMeshPeerProbe : IMeshPeerProbe
    {
        public string? LastCorrelationId { get; private set; }

        public Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCorrelationId = correlationId;
            IReadOnlyCollection<MeshPeerProbeResult> results =
            [
                new(
                    new NodeId("peer-one"),
                    new Uri("http://peer-one:8081"),
                    true,
                    new NodeId("peer-one"),
                    Guid.Parse("5e51a555-8447-40dd-ac45-4673cfcda720"),
                    1,
                    TimeSpan.FromMilliseconds(4),
                    null,
                    null),
                new(
                    new NodeId("peer-two"),
                    new Uri("http://peer-two:8081"),
                    false,
                    null,
                    null,
                    null,
                    TimeSpan.FromMilliseconds(15),
                    MeshErrorCodes.PeerUnreachable,
                    "The mesh peer is unreachable."),
            ];
            return Task.FromResult(results);
        }
    }

    private sealed class BlockingMeshPeerProbe : IMeshPeerProbe
    {
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyCollection<MeshPeerProbeResult>> ProbeAllAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private sealed record MeshResponse(
        string NodeId,
        Guid InstanceId,
        int ProtocolVersion,
        MeshPeerResponse[] Peers);

    private sealed record MeshPeerResponse(
        string NodeId,
        string InternalUrl,
        bool IsReachable,
        string? RemoteNodeId,
        Guid? RemoteInstanceId,
        int? ProtocolVersion,
        double DurationMilliseconds,
        string? ErrorCode,
        string? ErrorMessage);
}
