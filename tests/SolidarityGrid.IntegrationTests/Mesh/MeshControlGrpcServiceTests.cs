using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Contracts;
using SolidarityGrid.Contracts.Mesh.V1;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Mesh;

public sealed class MeshControlGrpcServiceTests
{
    [Fact]
    public async Task ValidProbeReturnsRuntimeIdentityProtocolAndUtcTimestamp()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);
        var identity = factory.Services.GetRequiredService<IMeshNodeIdentity>();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();

        var response = await CreateClient(channel).ProbeAsync(
            ValidRequest(),
            new Metadata { { "x-correlation-id", "grpc-correlation-42" } });

        var after = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds();
        Assert.Equal("test-node", response.ResponderNodeId);
        Assert.Equal(identity.InstanceId, Guid.Parse(response.ResponderInstanceId));
        Assert.Equal(MeshProtocol.CurrentVersion, response.ProtocolVersion);
        Assert.InRange(response.ReceivedAtUnixMilliseconds, before, after);
        Assert.True(factory.Logs.ContainsScope(
            "CorrelationId",
            "grpc-correlation-42"));
    }

    [Fact]
    public async Task UnknownCallerIsPermissionDenied()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);
        var request = ValidRequest();
        request.CallerNodeId = "unknown-node";

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await CreateClient(channel).ProbeAsync(request));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task LocalCallerIsInvalidArgument()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);
        var request = ValidRequest();
        request.CallerNodeId = "test-node";

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await CreateClient(channel).ProbeAsync(request));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task InvalidCallerInstanceIdIsInvalidArgument()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);
        var request = ValidRequest();
        request.CallerInstanceId = "not-a-guid";

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await CreateClient(channel).ProbeAsync(request));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task IncompatibleProtocolIsFailedPrecondition()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);
        var request = ValidRequest();
        request.ProtocolVersion = MeshProtocol.CurrentVersion + 1;

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await CreateClient(channel).ProbeAsync(request));

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
    }

    [Fact]
    public async Task CallerCancellationIsObserved()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Record.ExceptionAsync(
            async () => await CreateClient(channel).ProbeAsync(
                ValidRequest(),
                cancellationToken: cancellation.Token));

        Assert.True(
            exception is OperationCanceledException ||
            exception is RpcException { StatusCode: StatusCode.Cancelled });
    }

    [Fact]
    public async Task ProbeDoesNotCreateOrModifyPayments()
    {
        using var factory = new MeshServerFactory();
        using var channel = CreateChannel(factory);

        await CreateClient(channel).ProbeAsync(ValidRequest());

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = factory.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM payments;";
        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0, count);
    }

    private static GrpcChannel CreateChannel(ValidNodeFactory factory) =>
        GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions
            {
                HttpHandler = factory.Server.CreateHandler(),
            });

    private static MeshControl.MeshControlClient CreateClient(GrpcChannel channel) =>
        new(channel);

    private static ProbeRequest ValidRequest() =>
        new()
        {
            CallerNodeId = "peer-one",
            CallerInstanceId = Guid.NewGuid().ToString("D"),
            ProtocolVersion = MeshProtocol.CurrentVersion,
            SentAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

    private sealed class MeshServerFactory : ValidNodeFactory
    {
        public ScopeCapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(
            Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(
                services => services.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    private sealed class ScopeCapturingLoggerProvider :
        ILoggerProvider,
        ISupportExternalScope
    {
        private readonly ConcurrentBag<IReadOnlyDictionary<string, object?>> _scopes = [];
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        public ILogger CreateLogger(string categoryName) =>
            new ScopeCapturingLogger(_scopeProvider, _scopes);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public bool ContainsScope(string key, string value) =>
            _scopes.Any(scope =>
                scope.TryGetValue(key, out var candidate) &&
                string.Equals(candidate?.ToString(), value, StringComparison.Ordinal));

        public void Dispose()
        {
        }

        private sealed class ScopeCapturingLogger(
            IExternalScopeProvider scopeProvider,
            ConcurrentBag<IReadOnlyDictionary<string, object?>> scopes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                scopeProvider.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                scopeProvider.ForEachScope(
                    (scope, target) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object>> values)
                        {
                            target.Add(values.ToDictionary(
                                item => item.Key,
                                item => (object?)item.Value,
                                StringComparer.Ordinal));
                        }
                    },
                    scopes);
            }
        }
    }
}
