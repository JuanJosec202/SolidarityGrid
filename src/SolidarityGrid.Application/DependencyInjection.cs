using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidarityGrid.Application.Payments;

namespace SolidarityGrid.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SubmitPaymentUseCase>();
        services.AddScoped<GetPaymentByIdUseCase>();
        return services;
    }
}
