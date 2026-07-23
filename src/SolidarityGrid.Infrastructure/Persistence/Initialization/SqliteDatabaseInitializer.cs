using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidarityGrid.Infrastructure.Persistence.Configuration;

namespace SolidarityGrid.Infrastructure.Persistence.Initialization;

public sealed partial class SqliteDatabaseInitializer(
    SolidarityGridDbContext dbContext,
    IOptions<PersistenceOptions> persistenceOptions,
    DatabaseInitializationState initializationState,
    ILogger<SqliteDatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsSqlite())
        {
            throw new InvalidOperationException(
                "SolidarityGridDbContext must use the SQLite provider.");
        }

        var databaseDirectory = Path.GetDirectoryName(
            persistenceOptions.Value.DatabasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
        {
            throw new InvalidOperationException(
                "The SQLite database path must have a parent directory.");
        }

        Directory.CreateDirectory(databaseDirectory);

        var pendingMigrations = (
            await dbContext.Database.GetPendingMigrationsAsync(cancellationToken))
            .ToArray();
        foreach (var migration in pendingMigrations)
        {
            LogApplyingMigration(logger, migration);
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
        initializationState.MarkInitialized();
        LogInitializationCompleted(logger, pendingMigrations.Length);
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Applying SQLite migration {Migration}.")]
    private static partial void LogApplyingMigration(ILogger logger, string migration);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "SQLite initialization completed with {AppliedMigrationCount} newly applied migrations.")]
    private static partial void LogInitializationCompleted(
        ILogger logger,
        int appliedMigrationCount);
}
