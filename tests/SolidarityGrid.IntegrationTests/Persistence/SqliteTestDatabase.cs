using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SolidarityGrid.Infrastructure.Persistence;
using SolidarityGrid.Infrastructure.Persistence.Configuration;
using SolidarityGrid.Infrastructure.Persistence.Initialization;

namespace SolidarityGrid.IntegrationTests.Persistence;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly PersistenceOptions _persistenceOptions;

    public SqliteTestDatabase(int busyTimeoutMilliseconds = 2750)
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "SolidarityGrid.Tests",
            Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DirectoryPath, "node.db");
        _persistenceOptions = new PersistenceOptions
        {
            DatabasePath = DatabasePath,
            BusyTimeoutMilliseconds = busyTimeoutMilliseconds,
        };
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public int BusyTimeoutMilliseconds => _persistenceOptions.BusyTimeoutMilliseconds;

    public SolidarityGridDbContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
        var interceptor = new SqliteConnectionInterceptor(
            Options.Create(_persistenceOptions));
        var options = new DbContextOptionsBuilder<SolidarityGridDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new SolidarityGridDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        var initializer = new SqliteDatabaseInitializer(
            context,
            Options.Create(_persistenceOptions),
            new DatabaseInitializationState(),
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.Yield();

        DeleteIfExists(DatabasePath);
        DeleteIfExists($"{DatabasePath}-wal");
        DeleteIfExists($"{DatabasePath}-shm");
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: false);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
