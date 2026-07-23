using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace SolidarityGrid.Node.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(
        HttpContext context,
        HealthReport report,
        string nodeId,
        TimeProvider timeProvider)
    {
        var status = context.Request.Path.Value?.EndsWith("/live", StringComparison.Ordinal) == true
            ? "live"
            : report.Status == HealthStatus.Healthy ? "ready" : "not-ready";

        return context.Response.WriteAsJsonAsync(
            new
            {
                status,
                nodeId,
                timestampUtc = timeProvider.GetUtcNow(),
            },
            context.RequestAborted);
    }
}
