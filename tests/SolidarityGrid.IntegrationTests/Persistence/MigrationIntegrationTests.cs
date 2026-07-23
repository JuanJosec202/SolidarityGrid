using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace SolidarityGrid.IntegrationTests.Persistence;

public sealed class MigrationIntegrationTests
{
    [Fact]
    public async Task ExactlyOneMigrationCanRollbackAndReapplyCleanly()
    {
        await using var database = new SqliteTestDatabase();
        Directory.CreateDirectory(database.DirectoryPath);
        await using var context = database.CreateContext();
        var migrations = context.Database.GetMigrations().ToArray();
        var migration = Assert.Single(migrations);
        Assert.EndsWith("_InitialNodePaymentStorage", migration, StringComparison.Ordinal);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(migration);
        Assert.Equal([migration], await context.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync(Migration.InitialDatabase);
        Assert.False(await TableExistsAsync(context, "payments"));
        await migrator.MigrateAsync(migration);
        Assert.True(await TableExistsAsync(context, "payments"));
        Assert.True(await TableExistsAsync(context, "__EFMigrationsHistory"));
    }

    [Fact]
    public async Task MigrationScriptContainsOnlySqliteSchema()
    {
        await using var database = new SqliteTestDatabase();
        Directory.CreateDirectory(database.DirectoryPath);
        await using var context = database.CreateContext();
        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE \"payments\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlServer", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CURRENT_TIMESTAMP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("randomblob", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TemporaryDatabaseFilesAreRemoved()
    {
        var database = new SqliteTestDatabase();
        var directory = database.DirectoryPath;
        await database.InitializeAsync();
        Assert.True(File.Exists(database.DatabasePath));
        Assert.True(Directory.Exists(directory));

        await database.DisposeAsync();

        Assert.False(Directory.Exists(directory));
    }

    private static async Task<bool> TableExistsAsync(
        DbContext context,
        string tableName)
    {
        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync();
        }

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}
