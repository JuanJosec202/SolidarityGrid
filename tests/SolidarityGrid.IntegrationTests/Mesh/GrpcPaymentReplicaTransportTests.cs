using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh;
using SolidarityGrid.Infrastructure.Mesh.Configuration;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Mesh;

public sealed class GrpcPaymentReplicaTransportTests
{
    [Fact]
    public async Task ValidResponsePreservesPayloadAndCorrelation()
    {
        var behavior = new ReplicaBehavior("node-b");
        await using var server = await TestServer.StartAsync(behavior);
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var replica = Replica();
            var result = Assert.Single(await transport.ReplicateAsync(
                replica,
                "replica-correlation",
                CancellationToken.None));

            Assert.True(result.Stored);
            Assert.Equal(replica, behavior.LastReplica);
            Assert.Equal("replica-correlation", behavior.LastCorrelationId);
            Assert.Equal("node-a", behavior.LastCallerNodeId);
        }
    }

    [Theory]
    [InlineData(StatusCode.Unavailable, PaymentReplicaErrorCodes.PeerUnavailable)]
    [InlineData(StatusCode.AlreadyExists, PaymentReplicaErrorCodes.Conflict)]
    [InlineData(StatusCode.InvalidArgument, PaymentReplicaErrorCodes.Invalid)]
    [InlineData(
        StatusCode.FailedPrecondition,
        PaymentReplicaErrorCodes.ProtocolMismatch)]
    public async Task RpcFailuresBecomeNeutralResults(
        StatusCode statusCode,
        string expectedCode)
    {
        await using var server = await TestServer.StartAsync(
            new ReplicaBehavior("node-b") { FailureStatus = statusCode });
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(await transport.ReplicateAsync(
                Replica(),
                "failure",
                CancellationToken.None));

            Assert.False(result.Stored);
            Assert.Equal(expectedCode, result.ErrorCode);
        }
    }

    [Fact]
    public async Task DeadlineExceededBecomesNeutralResult()
    {
        await using var server = await TestServer.StartAsync(
            new ReplicaBehavior("node-b")
            {
                Delay = TimeSpan.FromMilliseconds(500),
            });
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)],
            timeoutMilliseconds: 50);
        using (pool)
        {
            var result = Assert.Single(await transport.ReplicateAsync(
                Replica(),
                "deadline",
                CancellationToken.None));

            Assert.False(result.Stored);
            Assert.Equal(
                PaymentReplicaErrorCodes.DeadlineExceeded,
                result.ErrorCode);
        }
    }

    [Fact]
    public async Task WrongResponderIdentityIsRejected()
    {
        await using var server = await TestServer.StartAsync(
            new ReplicaBehavior("different-node"));
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(await transport.ReplicateAsync(
                Replica(),
                "identity",
                CancellationToken.None));

            Assert.False(result.Stored);
            Assert.Equal(PaymentReplicaErrorCodes.Failed, result.ErrorCode);
        }
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        await using var server = await TestServer.StartAsync(
            new ReplicaBehavior("node-b")
            {
                Delay = TimeSpan.FromSeconds(5),
            });
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)],
            timeoutMilliseconds: 5_000);
        using (pool)
        using (var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(50)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transport.ReplicateAsync(
                    Replica(),
                    "cancel",
                    cancellation.Token));
        }
    }

    [Fact]
    public async Task BothPeersAreCalledInParallel()
    {
        var gate = new ParallelGate(2);
        await using var first = await TestServer.StartAsync(
            new ReplicaBehavior("node-b") { Gate = gate });
        await using var second = await TestServer.StartAsync(
            new ReplicaBehavior("node-c") { Gate = gate });
        var (transport, pool) = CreateTransport(
        [
            new MeshPeer(new NodeId("node-b"), first.Address),
            new MeshPeer(new NodeId("node-c"), second.Address),
        ]);
        using (pool)
        {
            var results = await transport.ReplicateAsync(
                Replica(),
                "parallel",
                CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.True(result.Stored));
        }
    }

    private static (GrpcPaymentReplicaTransport Transport, GrpcMeshChannelPool Pool)
        CreateTransport(
            IReadOnlyCollection<MeshPeer> peers,
            int timeoutMilliseconds = 1_000)
    {
        var directory = new StubDirectory(peers);
        var identity = new StubIdentity(
            new NodeId("node-a"),
            Guid.Parse("6ba41d75-ac28-4765-9e99-6bc0545c5c06"));
        var pool = new GrpcMeshChannelPool(directory);
        return (
            new GrpcPaymentReplicaTransport(
                directory,
                identity,
                pool,
                Options.Create(new MeshTransportOptions
                {
                    ProbeTimeoutMilliseconds = timeoutMilliseconds,
                }),
                TimeProvider.System,
                NullLogger<GrpcPaymentReplicaTransport>.Instance),
            pool);
    }

    private static PaymentReplica Replica()
    {
        var created = new DateTimeOffset(
            2026,
            7,
            25,
            13,
            0,
            0,
            TimeSpan.Zero);
        return new PaymentReplica(
            Guid.Parse("3ee0d78d-9075-4574-a7f9-ab833d7188ec"),
            new IdempotencyKey("TRANSPORT-1"),
            new Money(75.25m, "cop"),
            created,
            created.AddSeconds(1));
    }

    private sealed class StubDirectory(
        IReadOnlyCollection<MeshPeer> peers) : IMeshPeerDirectory
    {
        public IReadOnlyCollection<MeshPeer> GetPeers() => peers;

        public bool Contains(NodeId nodeId) =>
            peers.Any(peer => peer.NodeId == nodeId);
    }

    private sealed record StubIdentity(NodeId NodeId, Guid InstanceId)
        : IMeshNodeIdentity;

    private sealed class ReplicaBehavior(string responderNodeId)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public StatusCode FailureStatus { get; init; } = StatusCode.OK;

        public TimeSpan Delay { get; init; }

        public ParallelGate? Gate { get; init; }

        public string? LastCorrelationId { get; private set; }

        public string? LastCallerNodeId { get; private set; }

        public PaymentReplica? LastReplica { get; private set; }

        public async Task<ReplicatePaymentResponse> ExecuteAsync(
            ReplicatePaymentRequest request,
            ServerCallContext context)
        {
            LastCorrelationId = context.RequestHeaders
                .FirstOrDefault(item => item.Key == "x-correlation-id")
                ?.Value;
            LastCallerNodeId = request.CallerNodeId;
            LastReplica = new PaymentReplica(
                Guid.Parse(request.PaymentId),
                new IdempotencyKey(request.IdempotencyKey),
                new Money(
                    decimal.Parse(
                        request.Amount,
                        System.Globalization.CultureInfo.InvariantCulture),
                    request.Currency),
                new DateTimeOffset(request.CreatedAtUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(request.ReplicatedAtUtcTicks, TimeSpan.Zero));

            if (Gate is not null)
            {
                await Gate.SignalAndWaitAsync(context.CancellationToken);
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, context.CancellationToken);
            }

            if (FailureStatus != StatusCode.OK)
            {
                throw new RpcException(new Status(FailureStatus, "Test failure."));
            }

            return new ReplicatePaymentResponse
            {
                ResponderNodeId = responderNodeId,
                ResponderInstanceId = InstanceId.ToString("D"),
                PaymentId = request.PaymentId,
                Stored = true,
                AlreadyExisted = false,
                Status = "Replicated",
                Version = 2,
            };
        }
    }

    private sealed class StubService(ReplicaBehavior behavior)
        : MeshControl.MeshControlBase
    {
        public override Task<ReplicatePaymentResponse> ReplicatePayment(
            ReplicatePaymentRequest request,
            ServerCallContext context) =>
            behavior.ExecuteAsync(request, context);
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private TestServer(WebApplication application, Uri address)
        {
            _application = application;
            Address = address;
        }

        public Uri Address { get; }

        public static async Task<TestServer> StartAsync(ReplicaBehavior behavior)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "IntegrationTest",
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(
                    IPAddress.Loopback,
                    0,
                    listenOptions =>
                        listenOptions.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(behavior);
            var application = builder.Build();
            application.MapGrpcService<StubService>();
            await application.StartAsync();
            var server = application.Services.GetRequiredService<IServer>();
            var address = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
            return new TestServer(application, new Uri(address));
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }

    private sealed class ParallelGate(int participants)
    {
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == participants)
            {
                _ready.TrySetResult();
            }

            await _ready.Task.WaitAsync(cancellationToken);
        }
    }
}
