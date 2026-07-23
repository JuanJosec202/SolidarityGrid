using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Infrastructure.Persistence.Configuration;

namespace SolidarityGrid.Infrastructure.Persistence;

internal static class SqliteDbContextOptionsConfigurator
{
    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        PersistenceOptions persistenceOptions,
        SqliteConnectionInterceptor interceptor)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = persistenceOptions.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();

        optionsBuilder
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(SolidarityGridDbContext).Assembly.FullName))
            .AddInterceptors(interceptor);
    }
}
