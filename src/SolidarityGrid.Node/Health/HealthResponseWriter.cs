using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SolidarityGrid.Node.Configuration;

namespace SolidarityGrid.Node.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var nodeOptions = context.RequestServices.GetRequiredService<IOptions<NodeOptions>>().Value;
        var timeProvider = context.RequestServices.GetRequiredService<TimeProvider>();
        var status = context.Request.Path.Value?.EndsWith("/live", StringComparison.Ordinal) == true
            ? "live"
            : report.Status == HealthStatus.Healthy ? "ready" : "not-ready";

        return context.Response.WriteAsJsonAsync(
            new
            {
                status,
                nodeId = nodeOptions.NodeId,
                timestampUtc = timeProvider.GetUtcNow(),
            },
            context.RequestAborted);
    }
}
