using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using SolidarityGrid.Infrastructure.Persistence.Configuration;

namespace SolidarityGrid.Infrastructure.Persistence.Design;

public sealed class SolidarityGridDbContextFactory
    : IDesignTimeDbContextFactory<SolidarityGridDbContext>
{
    public SolidarityGridDbContext CreateDbContext(string[] args)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SolidarityGrid",
            "DesignTime");
        Directory.CreateDirectory(directory);

        var persistenceOptions = new PersistenceOptions
        {
            DatabasePath = Path.Combine(directory, "solidarity-grid-design.db"),
            BusyTimeoutMilliseconds = 5000,
        };
        var interceptor = new SqliteConnectionInterceptor(
            Options.Create(persistenceOptions));
        var optionsBuilder = new DbContextOptionsBuilder<SolidarityGridDbContext>();
        SqliteDbContextOptionsConfigurator.Configure(
            optionsBuilder,
            persistenceOptions,
            interceptor);

        return new SolidarityGridDbContext(optionsBuilder.Options);
    }
}
