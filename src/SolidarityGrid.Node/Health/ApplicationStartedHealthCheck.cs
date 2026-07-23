using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SolidarityGrid.Node.Health;

public sealed class ApplicationStartedHealthCheck(IHostApplicationLifetime applicationLifetime)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = applicationLifetime.ApplicationStarted.IsCancellationRequested
            ? HealthCheckResult.Healthy("Application startup completed.")
            : HealthCheckResult.Unhealthy("Application startup is not complete.");

        return Task.FromResult(result);
    }
}
