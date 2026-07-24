using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidarityGrid.Application.Payments;
using SolidarityGrid.Application.Payments.Coordination;
using SolidarityGrid.Application.Payments.Replication;

namespace SolidarityGrid.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SubmitPaymentUseCase>();
        services.AddScoped<GetPaymentByIdUseCase>();
        services.AddScoped<ReceivePaymentReplicaUseCase>();
        services.AddScoped<PaymentReplicationCoordinator>();
        services.AddScoped<ReceivePaymentClaimUseCase>();
        services.AddScoped<ReceivePaymentProcessingStartedUseCase>();
        services.AddScoped<ReceivePaymentLeaseRenewalUseCase>();
        services.AddScoped<ReceivePaymentCompletionUseCase>();
        services.AddScoped<PaymentOwnershipCoordinator>();
        services.AddScoped<PaymentProcessingOrchestrator>();
        services.AddScoped<IPaymentProcessor>(serviceProvider =>
            serviceProvider.GetRequiredService<PaymentProcessingOrchestrator>());
        return services;
    }
}
