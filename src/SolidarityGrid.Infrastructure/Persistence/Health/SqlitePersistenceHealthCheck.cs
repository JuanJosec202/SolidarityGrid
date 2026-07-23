using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SolidarityGrid.Infrastructure.Persistence.Initialization;

namespace SolidarityGrid.Infrastructure.Persistence.Health;

public sealed class SqlitePersistenceHealthCheck(
    IDbContextFactory<SolidarityGridDbContext> dbContextFactory,
    DatabaseInitializationState initializationState) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!initializationState.IsInitialized)
        {
            return HealthCheckResult.Unhealthy(
                "SQLite initialization has not completed.");
        }

        try
        {
            await using var dbContext =
                await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("SQLite is not accessible.");
            }

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1
                ? HealthCheckResult.Healthy("SQLite is ready.")
                : HealthCheckResult.Unhealthy("SQLite readiness query failed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "SQLite readiness check failed.",
                exception);
        }
    }
}
