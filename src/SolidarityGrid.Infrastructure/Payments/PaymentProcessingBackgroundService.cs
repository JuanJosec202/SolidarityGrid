using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolidarityGrid.Application.Payments.Coordination;

namespace SolidarityGrid.Infrastructure.Payments;

public sealed class PaymentProcessingBackgroundService(
    IServiceScopeFactory scopeFactory,
    PaymentProcessingOptions options,
    TimeProvider timeProvider) : BackgroundService
{
    public async Task<int> ExecuteCycleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cycle = scope.ServiceProvider
            .GetRequiredService<PaymentProcessingCycle>();
        return await cycle.ExecuteAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(options.ScanIntervalMilliseconds),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
