using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SolidarityGrid.Infrastructure.Persistence;
using SolidarityGrid.Infrastructure.Persistence.Health;
using SolidarityGrid.Infrastructure.Persistence.Initialization;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Persistence;

public sealed class SqlitePersistenceHealthCheckTests
{
    [Fact]
    public async Task InitializationNotCompletedIsUnhealthy()
    {
        var healthCheck = CreateHealthCheck(
            Path.Combine(Path.GetTempPath(), "missing-node.db"),
            initialized: false);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("initialization", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InaccessibleDatabaseIsUnhealthyAfterInitialization()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "SolidarityGrid.Tests",
            Guid.NewGuid().ToString("N"),
            "missing.db");
        var healthCheck = CreateHealthCheck(missingPath, initialized: true);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private static SqlitePersistenceHealthCheck CreateHealthCheck(
        string databasePath,
        bool initialized)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
        var options = new DbContextOptionsBuilder<SolidarityGridDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var state = new DatabaseInitializationState();
        if (initialized)
        {
            state.MarkInitialized();
        }

        return new SqlitePersistenceHealthCheck(
            new TestDbContextFactory(options),
            state);
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<SolidarityGridDbContext> options)
        : IDbContextFactory<SolidarityGridDbContext>
    {
        public SolidarityGridDbContext CreateDbContext() => new(options);
    }
}
