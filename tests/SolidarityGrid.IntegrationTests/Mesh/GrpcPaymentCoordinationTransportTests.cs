using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh;
using SolidarityGrid.Infrastructure.Mesh.Configuration;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Mesh;

public sealed class GrpcPaymentCoordinationTransportTests
{
    [Fact]
    public async Task ClaimPreservesPayloadAndCorrelation()
    {
        var behavior = new CoordinationBehavior("node-b");
        await using var server = await TestServer.StartAsync(behavior);
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var request = Claim();

            var result = Assert.Single(await transport.TryClaimAsync(
                request,
                "coordination-correlation",
                CancellationToken.None));

            Assert.True(result.Granted);
            Assert.Equal(request, behavior.LastClaim);
            Assert.Equal(
                "coordination-correlation",
                behavior.LastCorrelationId);
        }
    }

    [Fact]
    public async Task AllMutationOperationsReturnOneResultPerPeer()
    {
        var behavior = new CoordinationBehavior("node-b");
        await using var server = await TestServer.StartAsync(behavior);
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var command = Command();

            var started = Assert.Single(await transport.StartProcessingAsync(
                command,
                "start",
                CancellationToken.None));
            var renewed = Assert.Single(await transport.RenewLeaseAsync(
                command with
                {
                    LeaseExpiresAtUtc =
                        command.OccurredAtUtc.AddMinutes(2),
                },
                "renew",
                CancellationToken.None));
            var completed = Assert.Single(await transport.CompleteAsync(
                command,
                "complete",
                CancellationToken.None));

            Assert.True(started.Applied);
            Assert.True(renewed.Applied);
            Assert.True(completed.Applied);
            Assert.Equal(["start", "renew", "complete"], behavior.Operations);
        }
    }

    [Theory]
    [InlineData(
        StatusCode.Unavailable,
        PaymentCoordinationErrorCodes.PeerUnavailable)]
    [InlineData(
        StatusCode.DeadlineExceeded,
        PaymentCoordinationErrorCodes.DeadlineExceeded)]
    public async Task RpcFailuresBecomeNeutralResults(
        StatusCode statusCode,
        string expectedError)
    {
        await using var server = await TestServer.StartAsync(
            new CoordinationBehavior("node-b") { FailureStatus = statusCode });
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(await transport.TryClaimAsync(
                Claim(),
                "failure",
                CancellationToken.None));

            Assert.False(result.Granted);
            Assert.Equal(expectedError, result.ErrorCode);
        }
    }

    [Fact]
    public async Task DeadlineExceededBecomesNeutralResult()
    {
        await using var server = await TestServer.StartAsync(
            new CoordinationBehavior("node-b")
            {
                Delay = TimeSpan.FromMilliseconds(500),
            });
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)],
            timeoutMilliseconds: 50);
        using (pool)
        {
            var result = Assert.Single(await transport.TryClaimAsync(
                Claim(),
                "deadline",
                CancellationToken.None));

            Assert.False(result.Granted);
            Assert.Equal(
                PaymentCoordinationErrorCodes.DeadlineExceeded,
                result.ErrorCode);
        }
    }

    [Fact]
    public async Task WrongRemoteIdentityIsRejected()
    {
        await using var server = await TestServer.StartAsync(
            new CoordinationBehavior("wrong-node"));
        var (transport, pool) = CreateTransport(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(await transport.TryClaimAsync(
                Claim(),
                "identity",
                CancellationToken.None));

            Assert.False(result.Granted);
            Assert.Equal(PaymentCoordinationErrorCodes.Failed, result.ErrorCode);
        }
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        await using var server = await TestServer.StartAsync(
            new CoordinationBehavior("node-b")
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
                () => transport.TryClaimAsync(
                    Claim(),
                    "cancel",
                    cancellation.Token));
        }
    }

    [Fact]
    public async Task BothPeersAreCalledInParallel()
    {
        var gate = new ParallelGate(2);
        await using var first = await TestServer.StartAsync(
            new CoordinationBehavior("node-b") { Gate = gate });
        await using var second = await TestServer.StartAsync(
            new CoordinationBehavior("node-c") { Gate = gate });
        var (transport, pool) = CreateTransport(
        [
            new MeshPeer(new NodeId("node-b"), first.Address),
            new MeshPeer(new NodeId("node-c"), second.Address),
        ]);
        using (pool)
        {
            var results = await transport.TryClaimAsync(
                Claim(),
                "parallel",
                CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.True(result.Granted));
        }
    }

    private static (
        GrpcPaymentCoordinationTransport Transport,
        GrpcMeshChannelPool Pool) CreateTransport(
        IReadOnlyCollection<MeshPeer> peers,
        int timeoutMilliseconds = 1_000)
    {
        var directory = new StubDirectory(peers);
        var identity = new StubIdentity(
            new NodeId("node-a"),
            Guid.Parse("c6d26472-ad58-4d8e-b826-18c89e93b215"));
        var pool = new GrpcMeshChannelPool(directory);
        return (
            new GrpcPaymentCoordinationTransport(
                directory,
                identity,
                pool,
                Options.Create(new MeshTransportOptions
                {
                    ProbeTimeoutMilliseconds = timeoutMilliseconds,
                }),
                TimeProvider.System),
            pool);
    }

    private static PaymentClaimRequest Claim()
    {
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            26,
            12,
            0,
            0,
            TimeSpan.Zero);
        return new PaymentClaimRequest(
            Guid.Parse("da83f3b3-bceb-47f7-9d87-41beb53d9041"),
            new NodeId("node-a"),
            3,
            occurredAt.AddMinutes(1),
            occurredAt);
    }

    private static PaymentCoordinationCommand Command() =>
        new(
            Claim().PaymentId,
            new NodeId("node-a"),
            3,
            Claim().OccurredAtUtc.AddSeconds(1));

    private sealed class StubDirectory(
        IReadOnlyCollection<MeshPeer> peers) : IMeshPeerDirectory
    {
        public IReadOnlyCollection<MeshPeer> GetPeers() => peers;

        public bool Contains(NodeId nodeId) =>
            peers.Any(peer => peer.NodeId == nodeId);
    }

    private sealed record StubIdentity(NodeId NodeId, Guid InstanceId)
        : IMeshNodeIdentity;

    private sealed class CoordinationBehavior(string responderNodeId)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public StatusCode FailureStatus { get; init; } = StatusCode.OK;

        public TimeSpan Delay { get; init; }

        public ParallelGate? Gate { get; init; }

        public PaymentClaimRequest? LastClaim { get; private set; }

        public string? LastCorrelationId { get; private set; }

        public List<string> Operations { get; } = [];

        public async Task<TryClaimPaymentResponse> ClaimAsync(
            TryClaimPaymentRequest request,
            ServerCallContext context)
        {
            LastCorrelationId = context.RequestHeaders
                .FirstOrDefault(item => item.Key == "x-correlation-id")
                ?.Value;
            LastClaim = new PaymentClaimRequest(
                Guid.Parse(request.PaymentId),
                new NodeId(request.OwnerNodeId),
                request.Term,
                new DateTimeOffset(
                    request.LeaseExpiresAtUtcTicks,
                    TimeSpan.Zero),
                new DateTimeOffset(request.OccurredAtUtcTicks, TimeSpan.Zero));
            await WaitOrFailAsync(context);
            return new TryClaimPaymentResponse
            {
                ResponderNodeId = responderNodeId,
                ResponderInstanceId = InstanceId.ToString("D"),
                PaymentId = request.PaymentId,
                Granted = true,
                CurrentStatus = "Claimed",
                CurrentOwnerNodeId = request.OwnerNodeId,
                CurrentTerm = request.Term,
                LeaseExpiresAtUtcTicks = request.LeaseExpiresAtUtcTicks,
                ErrorCode = PaymentCoordinationErrorCodes.ClaimGranted,
            };
        }

        public async Task<PaymentCoordinationResponse> MutationAsync(
            string operation,
            string paymentId,
            string ownerNodeId,
            long term,
            ServerCallContext context)
        {
            Operations.Add(operation);
            await WaitOrFailAsync(context);
            return new PaymentCoordinationResponse
            {
                ResponderNodeId = responderNodeId,
                ResponderInstanceId = InstanceId.ToString("D"),
                PaymentId = paymentId,
                Applied = true,
                CurrentStatus = operation,
                CurrentOwnerNodeId = ownerNodeId,
                CurrentTerm = term,
            };
        }

        private async Task WaitOrFailAsync(ServerCallContext context)
        {
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
                throw new RpcException(new Status(
                    FailureStatus,
                    "Expected test failure."));
            }
        }
    }

    private sealed class StubService(CoordinationBehavior behavior)
        : MeshControl.MeshControlBase
    {
        public override Task<TryClaimPaymentResponse> TryClaimPayment(
            TryClaimPaymentRequest request,
            ServerCallContext context) =>
            behavior.ClaimAsync(request, context);

        public override Task<PaymentCoordinationResponse> StartPaymentProcessing(
            StartPaymentProcessingRequest request,
            ServerCallContext context) =>
            behavior.MutationAsync(
                "start",
                request.PaymentId,
                request.OwnerNodeId,
                request.Term,
                context);

        public override Task<PaymentCoordinationResponse> RenewPaymentLease(
            RenewPaymentLeaseRequest request,
            ServerCallContext context) =>
            behavior.MutationAsync(
                "renew",
                request.PaymentId,
                request.OwnerNodeId,
                request.Term,
                context);

        public override Task<PaymentCoordinationResponse> CompletePayment(
            CompletePaymentRequest request,
            ServerCallContext context) =>
            behavior.MutationAsync(
                "complete",
                request.PaymentId,
                request.OwnerNodeId,
                request.Term,
                context);
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

        public static async Task<TestServer> StartAsync(
            CoordinationBehavior behavior)
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
