using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Abstractions;
using SolidarityGrid.Application.Mesh;
using SolidarityGrid.Application.Mesh.Health;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Application.Payments.Replication;
using SolidarityGrid.Infrastructure.Identifiers;
using SolidarityGrid.Infrastructure.Mesh;
using SolidarityGrid.Infrastructure.Mesh.Configuration;
using SolidarityGrid.Infrastructure.Persistence;
using SolidarityGrid.Infrastructure.Persistence.Configuration;
using SolidarityGrid.Infrastructure.Persistence.Health;
using SolidarityGrid.Infrastructure.Persistence.Initialization;
using SolidarityGrid.Infrastructure.Payments;
using SolidarityGrid.Infrastructure.Payments.Configuration;

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
        services
            .AddOptions<MeshTransportOptions>()
            .Bind(configuration.GetRequiredSection(MeshTransportOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<MeshTransportOptions>,
            MeshTransportOptionsValidator>();
        services
            .AddOptions<MeshFailureDetectorOptions>()
            .Bind(configuration.GetRequiredSection(
                MeshFailureDetectorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<MeshFailureDetectorOptions>,
            MeshFailureDetectorOptionsValidator>();
        services
            .AddOptions<PaymentProcessingOptions>()
            .Bind(configuration.GetRequiredSection(
                PaymentProcessingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<PaymentProcessingOptions>,
            PaymentProcessingOptionsValidator>();
        services.AddSingleton(serviceProvider =>
            serviceProvider
                .GetRequiredService<IOptions<PaymentProcessingOptions>>()
                .Value);
        services.AddSingleton<GrpcMeshChannelPool>();
        services.AddSingleton<IMeshPeerProbe, GrpcMeshPeerProbe>();
        services.AddSingleton<
            IPaymentReplicaTransport,
            GrpcPaymentReplicaTransport>();
        services.AddSingleton<
            IPaymentReplicationObserver,
            PaymentReplicationObserver>();
        services.AddSingleton<
            IPaymentCoordinationTransport,
            GrpcPaymentCoordinationTransport>();
        services.AddSingleton<
            IPaymentCoordinationObserver,
            PaymentCoordinationObserver>();
        services.AddSingleton<IMeshPeerHealthRegistry>(serviceProvider =>
        {
            var detector = serviceProvider
                .GetRequiredService<IOptions<MeshFailureDetectorOptions>>()
                .Value;
            return new InMemoryMeshPeerHealthRegistry(
                serviceProvider
                    .GetRequiredService<IMeshPeerDirectory>()
                    .GetPeers(),
                serviceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
                new MeshFailureDetectionThresholds(
                    TimeSpan.FromMilliseconds(detector.SuspectAfterMilliseconds),
                    TimeSpan.FromMilliseconds(
                        detector.UnreachableAfterMilliseconds)));
        });
        services.AddSingleton<MeshPeerHealthMonitor>();
        services.AddHostedService<MeshHeartbeatBackgroundService>();
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
        services.AddScoped<PaymentProcessingCycle>();
        services.AddHostedService<PaymentProcessingBackgroundService>();
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
