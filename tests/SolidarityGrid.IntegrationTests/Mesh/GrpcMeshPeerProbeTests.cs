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
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Mesh;
using SolidarityGrid.Infrastructure.Mesh.Configuration;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Mesh;

public sealed class GrpcMeshPeerProbeTests
{
    [Fact]
    public async Task SuccessfulProbePropagatesCorrelationAndRemoteIdentity()
    {
        var behavior = new ProbeBehavior("node-b");
        await using var server = await TestMeshServer.StartAsync(behavior);
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var results = await probe.ProbeAllAsync(
                "client-correlation-7",
                CancellationToken.None);

            var result = Assert.Single(results);
            Assert.True(result.IsReachable);
            Assert.Equal("node-b", result.RemoteNodeId?.Value);
            Assert.Equal(behavior.InstanceId, result.RemoteInstanceId);
            Assert.Equal(MeshProtocol.CurrentVersion, result.ProtocolVersion);
            Assert.Equal("client-correlation-7", behavior.LastCorrelationId);
            Assert.Equal("node-a", behavior.LastCallerNodeId);
            Assert.Equal(1, behavior.CallCount);
        }
    }

    [Fact]
    public async Task UnexpectedNodeIdBecomesIdentityMismatch()
    {
        await using var server = await TestMeshServer.StartAsync(
            new ProbeBehavior("different-node"));
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(
                await probe.ProbeAllAsync("correlation", CancellationToken.None));

            Assert.False(result.IsReachable);
            Assert.Equal(MeshErrorCodes.IdentityMismatch, result.ErrorCode);
        }
    }

    [Fact]
    public async Task InvalidRemoteInstanceIdBecomesIdentityMismatch()
    {
        await using var server = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-b")
            {
                ResponseInstanceId = "invalid",
            });
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(
                await probe.ProbeAllAsync("correlation", CancellationToken.None));

            Assert.False(result.IsReachable);
            Assert.Equal(MeshErrorCodes.IdentityMismatch, result.ErrorCode);
        }
    }

    [Fact]
    public async Task UnexpectedResponseProtocolBecomesProtocolMismatch()
    {
        await using var server = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-b")
            {
                ResponseProtocolVersion = MeshProtocol.CurrentVersion + 1,
            });
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(
                await probe.ProbeAllAsync("correlation", CancellationToken.None));

            Assert.False(result.IsReachable);
            Assert.Equal(MeshErrorCodes.ProtocolMismatch, result.ErrorCode);
        }
    }

    [Fact]
    public async Task ServerDeadlineDoesNotDiscardSuccessfulPeer()
    {
        await using var slowServer = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-b")
            {
                Delay = TimeSpan.FromMilliseconds(500),
            });
        await using var fastServer = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-c"));
        MeshPeer[] peers =
        [
            new(new NodeId("node-b"), slowServer.Address),
            new(new NodeId("node-c"), fastServer.Address),
        ];
        var (probe, pool) = CreateProbe(peers, timeoutMilliseconds: 100);
        using (pool)
        {
            var results = await probe.ProbeAllAsync(
                "deadline-correlation",
                CancellationToken.None);

            Assert.Equal(2, results.Count);
            var slow = Assert.Single(results, result => result.NodeId.Value == "node-b");
            var fast = Assert.Single(results, result => result.NodeId.Value == "node-c");
            Assert.False(slow.IsReachable);
            Assert.Equal(MeshErrorCodes.DeadlineExceeded, slow.ErrorCode);
            Assert.True(fast.IsReachable);
        }
    }

    [Fact]
    public async Task PermissionDeniedIsTranslatedWithoutRetry()
    {
        var behavior = new ProbeBehavior("node-b")
        {
            FailureStatus = StatusCode.PermissionDenied,
        };
        await using var server = await TestMeshServer.StartAsync(behavior);
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(
                await probe.ProbeAllAsync("correlation", CancellationToken.None));

            Assert.False(result.IsReachable);
            Assert.Equal(MeshErrorCodes.PermissionDenied, result.ErrorCode);
            Assert.Equal(1, behavior.CallCount);
        }
    }

    [Fact]
    public async Task UnavailablePeerProducesNeutralResult()
    {
        var behavior = new ProbeBehavior("node-b")
        {
            FailureStatus = StatusCode.Unavailable,
        };
        await using var server = await TestMeshServer.StartAsync(behavior);
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)]);
        using (pool)
        {
            var result = Assert.Single(
                await probe.ProbeAllAsync("correlation", CancellationToken.None));

            Assert.False(result.IsReachable);
            Assert.Equal(MeshErrorCodes.PeerUnreachable, result.ErrorCode);
            Assert.Equal(1, behavior.CallCount);
        }
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        await using var server = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-b")
            {
                Delay = TimeSpan.FromSeconds(5),
            });
        var (probe, pool) = CreateProbe(
            [new MeshPeer(new NodeId("node-b"), server.Address)],
            timeoutMilliseconds: 5_000);
        using (pool)
        using (var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => probe.ProbeAllAsync("correlation", cancellation.Token));
        }
    }

    [Fact]
    public async Task AllPeersAreProbedInParallel()
    {
        var gate = new ParallelProbeGate(2);
        await using var firstServer = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-b") { Gate = gate });
        await using var secondServer = await TestMeshServer.StartAsync(
            new ProbeBehavior("node-c") { Gate = gate });
        MeshPeer[] peers =
        [
            new(new NodeId("node-b"), firstServer.Address),
            new(new NodeId("node-c"), secondServer.Address),
        ];
        var (probe, pool) = CreateProbe(peers);
        using (pool)
        {
            var results = await probe.ProbeAllAsync(
                "parallel-correlation",
                CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.True(result.IsReachable));
        }
    }

    private static (GrpcMeshPeerProbe Probe, GrpcMeshChannelPool Pool) CreateProbe(
        IReadOnlyCollection<MeshPeer> peers,
        int timeoutMilliseconds = 1_000)
    {
        var directory = new StubPeerDirectory(peers);
        var identity = new StubMeshNodeIdentity(
            new NodeId("node-a"),
            Guid.Parse("8fc5c5f2-fb44-492c-adcf-5ddfcbe63e20"));
        var pool = new GrpcMeshChannelPool(directory);
        var probe = new GrpcMeshPeerProbe(
            directory,
            identity,
            pool,
            Options.Create(new MeshTransportOptions
            {
                ProbeTimeoutMilliseconds = timeoutMilliseconds,
            }),
            TimeProvider.System,
            NullLogger<GrpcMeshPeerProbe>.Instance);
        return (probe, pool);
    }

    private sealed class StubPeerDirectory(
        IReadOnlyCollection<MeshPeer> peers) : IMeshPeerDirectory
    {
        public IReadOnlyCollection<MeshPeer> GetPeers() => peers;

        public bool Contains(NodeId nodeId) =>
            peers.Any(peer => peer.NodeId == nodeId);
    }

    private sealed record StubMeshNodeIdentity(
        NodeId NodeId,
        Guid InstanceId) : IMeshNodeIdentity;

    private sealed class ProbeBehavior(string responseNodeId)
    {
        public Guid InstanceId { get; } = Guid.NewGuid();

        public string ResponseNodeId { get; } = responseNodeId;

        public string? ResponseInstanceId { get; init; }

        public int ResponseProtocolVersion { get; init; } =
            MeshProtocol.CurrentVersion;

        public StatusCode FailureStatus { get; init; } = StatusCode.OK;

        public TimeSpan Delay { get; init; }

        public ParallelProbeGate? Gate { get; init; }

        public int CallCount { get; private set; }

        public string? LastCorrelationId { get; private set; }

        public string? LastCallerNodeId { get; private set; }

        public async Task<ProbeResponse> ExecuteAsync(
            ProbeRequest request,
            ServerCallContext context)
        {
            CallCount++;
            LastCallerNodeId = request.CallerNodeId;
            LastCorrelationId = context.RequestHeaders
                .FirstOrDefault(item => item.Key == "x-correlation-id")
                ?.Value;

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

            return new ProbeResponse
            {
                ResponderNodeId = ResponseNodeId,
                ResponderInstanceId = ResponseInstanceId ?? InstanceId.ToString("D"),
                ProtocolVersion = ResponseProtocolVersion,
                ReceivedAtUnixMilliseconds =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }
    }

    private sealed class StubMeshControlService(
        ProbeBehavior behavior) : MeshControl.MeshControlBase
    {
        public override Task<ProbeResponse> Probe(
            ProbeRequest request,
            ServerCallContext context) =>
            behavior.ExecuteAsync(request, context);
    }

    private sealed class TestMeshServer : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private TestMeshServer(WebApplication application, Uri address)
        {
            _application = application;
            Address = address;
        }

        public Uri Address { get; }

        public static async Task<TestMeshServer> StartAsync(ProbeBehavior behavior)
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
            application.MapGrpcService<StubMeshControlService>();
            await application.StartAsync();

            var server = application.Services.GetRequiredService<IServer>();
            var address = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
            return new TestMeshServer(application, new Uri(address));
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }

    private sealed class ParallelProbeGate(int participantCount)
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == participantCount)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(cancellationToken);
        }
    }
}
