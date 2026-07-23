using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Diagnostics;
using SolidarityGrid.Node.Payments;
using Xunit;

namespace SolidarityGrid.IntegrationTests;

public sealed class PaymentReplicationApiTests
{
    [Fact]
    public async Task OneAvailablePeerReturnsAcceptedReplicatedVersionTwo()
    {
        using var factory = new ControllableReplicationFactory();
        factory.Transport.AvailablePeers = 1;
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            CreatePost($"ONE-PEER-{Guid.NewGuid():N}", "one-peer-correlation"));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Replicated", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("version").GetInt64());
        Assert.Equal("one-peer-correlation", factory.Transport.LastCorrelationId);
    }

    [Fact]
    public async Task NoPeersReturns503AndRetryReplicatesSamePayment()
    {
        using var factory = new ControllableReplicationFactory();
        factory.Transport.AvailablePeers = 0;
        using var client = factory.CreateClient();
        var key = $"RETRY-{Guid.NewGuid():N}";

        using var unavailable = await client.SendAsync(
            CreatePost(key, "quorum-correlation"));
        var problem = await ReadJsonAsync(unavailable);
        var location = unavailable.Headers.Location;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(
            PaymentReplicaErrorCodes.QuorumUnavailable,
            problem.GetProperty("code").GetString());
        Assert.Equal(
            "quorum-correlation",
            problem.GetProperty("correlationId").GetString());
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            unavailable.Headers.RetryAfter?.Delta);
        Assert.NotNull(location);

        using var localBeforeRetry = await client.GetAsync(location);
        var received = await ReadJsonAsync(localBeforeRetry);
        var paymentId = received.GetProperty("paymentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, localBeforeRetry.StatusCode);
        Assert.Equal("Received", received.GetProperty("status").GetString());
        Assert.Equal(1, received.GetProperty("version").GetInt64());

        factory.Transport.AvailablePeers = 1;
        using var replay = await client.SendAsync(
            CreatePost(key, "retry-correlation"));
        var replicated = await ReadJsonAsync(replay);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(paymentId, replicated.GetProperty("paymentId").GetGuid());
        Assert.Equal("Replicated", replicated.GetProperty("status").GetString());
        Assert.Equal(2, replicated.GetProperty("version").GetInt64());
        Assert.True(replicated.GetProperty("isReplay").GetBoolean());
        Assert.NotEqual(HttpStatusCode.InternalServerError, unavailable.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, replay.StatusCode);
    }

    private static HttpRequestMessage CreatePost(string key, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/pay")
        {
            Content = JsonContent.Create(new { amount = 45.50m, currency = "COP" }),
        };
        request.Headers.Add(PaymentEndpoints.IdempotencyKeyHeaderName, key);
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        return request;
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return Assert.IsType<JsonDocument>(document).RootElement.Clone();
    }

    private sealed class ControllableReplicationFactory : ValidNodeFactory
    {
        public ControllableTransport Transport { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPaymentReplicaTransport>();
                services.AddSingleton<IPaymentReplicaTransport>(Transport);
            });
        }
    }

    public sealed class ControllableTransport : IPaymentReplicaTransport
    {
        public int AvailablePeers { get; set; } = 2;

        public string? LastCorrelationId { get; private set; }

        public Task<IReadOnlyCollection<PaymentReplicaResult>> ReplicateAsync(
            PaymentReplica replica,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCorrelationId = correlationId;
            IReadOnlyCollection<PaymentReplicaResult> results =
            [
                Result("peer-one", AvailablePeers >= 1),
                Result("peer-two", AvailablePeers >= 2),
            ];
            return Task.FromResult(results);
        }

        private static PaymentReplicaResult Result(string nodeId, bool stored) =>
            new(
                new NodeId(nodeId),
                stored,
                false,
                stored ? null : PaymentReplicaErrorCodes.PeerUnavailable,
                stored ? null : "Unavailable.");
    }
}
