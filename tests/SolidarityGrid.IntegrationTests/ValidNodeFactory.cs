using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Infrastructure.Payments;

namespace SolidarityGrid.IntegrationTests;

public class ValidNodeFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseDirectory;
    private readonly string _nodeId;
    private readonly bool _deleteDatabaseOnDispose;

    public ValidNodeFactory()
        : this(CreateDatabaseDirectory(), "test-node", deleteDatabaseOnDispose: true)
    {
    }

    internal ValidNodeFactory(
        string databaseDirectory,
        string nodeId,
        bool deleteDatabaseOnDispose)
    {
        _databaseDirectory = databaseDirectory;
        _nodeId = nodeId;
        _deleteDatabaseOnDispose = deleteDatabaseOnDispose;
    }

    public string DatabasePath => Path.Combine(_databaseDirectory, "node.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Node:NodeId"] = _nodeId,
                ["Node:PublicUrl"] = "http://localhost:5999",
                ["Node:InternalUrl"] = "http://test-node:8081",
                ["Node:Environment"] = "IntegrationTest",
                ["Node:Peers:0:NodeId"] = "peer-one",
                ["Node:Peers:0:Url"] = "http://peer-one:8081",
                ["Node:Peers:1:NodeId"] = "peer-two",
                ["Node:Peers:1:Url"] = "http://peer-two:8081",
                ["Mesh:ProbeTimeoutMilliseconds"] = "1000",
                ["MeshFailureDetector:HeartbeatIntervalMilliseconds"] = "30000",
                ["MeshFailureDetector:SuspectAfterMilliseconds"] = "40000",
                ["MeshFailureDetector:UnreachableAfterMilliseconds"] = "50000",
                ["Persistence:DatabasePath"] = DatabasePath,
                ["Persistence:BusyTimeoutMilliseconds"] = "5000",
            });
        });
        builder.ConfigureServices(services =>
        {
            var processingWorker = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType ==
                typeof(PaymentProcessingBackgroundService));
            services.Remove(processingWorker);
            services.RemoveAll<IPaymentReplicaTransport>();
            services.AddSingleton<
                IPaymentReplicaTransport,
                SuccessfulReplicaTransport>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (_deleteDatabaseOnDispose && Directory.Exists(_databaseDirectory))
        {
            Directory.Delete(_databaseDirectory, recursive: true);
        }
    }

    public static string CreateDatabaseDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            "SolidarityGrid.Tests",
            Guid.NewGuid().ToString("N"));

    public static void DeleteDatabaseDirectory(string databaseDirectory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(databaseDirectory))
        {
            Directory.Delete(databaseDirectory, recursive: true);
        }
    }

    private sealed class SuccessfulReplicaTransport : IPaymentReplicaTransport
    {
        public Task<IReadOnlyCollection<PaymentReplicaResult>> ReplicateAsync(
            PaymentReplica replica,
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyCollection<PaymentReplicaResult> results =
            [
                new(
                    new NodeId("peer-one"),
                    true,
                    false,
                    null,
                    null),
                new(
                    new NodeId("peer-two"),
                    true,
                    false,
                    null,
                    null),
            ];
            return Task.FromResult(results);
        }
    }
}
