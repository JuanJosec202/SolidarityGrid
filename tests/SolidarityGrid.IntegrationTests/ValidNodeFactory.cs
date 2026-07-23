using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace SolidarityGrid.IntegrationTests;

public sealed class ValidNodeFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseDirectory = Path.Combine(
        Path.GetTempPath(),
        "SolidarityGrid.Tests",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Node:NodeId"] = "test-node",
                ["Node:PublicUrl"] = "http://localhost:5999",
                ["Node:InternalUrl"] = "http://test-node:8080",
                ["Node:Environment"] = "IntegrationTest",
                ["Node:Peers:0:NodeId"] = "peer-one",
                ["Node:Peers:0:Url"] = "http://peer-one:8080",
                ["Node:Peers:1:NodeId"] = "peer-two",
                ["Node:Peers:1:Url"] = "http://peer-two:8080",
                ["Persistence:DatabasePath"] = Path.Combine(
                    _databaseDirectory,
                    "node.db"),
                ["Persistence:BusyTimeoutMilliseconds"] = "5000",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_databaseDirectory))
        {
            Directory.Delete(_databaseDirectory, recursive: true);
        }
    }
}
