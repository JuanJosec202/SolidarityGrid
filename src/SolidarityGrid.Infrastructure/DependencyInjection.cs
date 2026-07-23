using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Infrastructure.Identifiers;
using SolidarityGrid.Infrastructure.Persistence;
using SolidarityGrid.Infrastructure.Persistence.Configuration;
using SolidarityGrid.Infrastructure.Persistence.Health;
using SolidarityGrid.Infrastructure.Persistence.Initialization;

namespace SolidarityGrid.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        services
            .AddOptions<PersistenceOptions>()
            .Bind(configuration.GetRequiredSection(PersistenceOptions.SectionName))
            .PostConfigure(options =>
            {
                options.DatabasePath = PersistencePath.Normalize(
                    options.DatabasePath,
                    hostEnvironment.ContentRootPath);
            })
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<PersistenceOptions>,
            PersistenceOptionsValidator>();
        services.AddSingleton<SqliteConnectionInterceptor>();
        services.AddSingleton<IIdGenerator, SystemIdGenerator>();
        services.AddDbContextFactory<SolidarityGridDbContext>((serviceProvider, options) =>
        {
            var persistenceOptions = serviceProvider
                .GetRequiredService<IOptions<PersistenceOptions>>()
                .Value;
            var interceptor =
                serviceProvider.GetRequiredService<SqliteConnectionInterceptor>();
            SqliteDbContextOptionsConfigurator.Configure(
                options,
                persistenceOptions,
                interceptor);
        });

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IUnitOfWork, SolidarityGridUnitOfWork>();
        services.AddSingleton<DatabaseInitializationState>();
        services.AddScoped<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services
            .AddHealthChecks()
            .AddCheck<SqlitePersistenceHealthCheck>(
                "sqlite",
                tags: ["ready"]);

        return services;
    }
}
