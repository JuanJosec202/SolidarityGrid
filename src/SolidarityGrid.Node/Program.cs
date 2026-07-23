using System.Reflection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SolidarityGrid.Node.Configuration;
using SolidarityGrid.Node.Diagnostics;
using SolidarityGrid.Node.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<NodeOptions>()
    .Bind(builder.Configuration.GetRequiredSection(NodeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<NodeOptions>, NodeOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Items.TryGetValue(
                CorrelationIdMiddleware.ItemName,
                out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<ApplicationStartedHealthCheck>("application-started", tags: ["ready"]);

var app = builder.Build();
var nodeOptions = app.Services.GetRequiredService<IOptions<NodeOptions>>().Value;
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    name = "SolidarityGrid",
    version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown",
    nodeId = nodeOptions.NodeId,
    links = new
    {
        live = "/health/live",
        ready = "/health/ready",
        node = "/node",
    },
}));

app.MapGet("/node", () => Results.Ok(new
{
    nodeId = nodeOptions.NodeId,
    publicUrl = nodeOptions.PublicUrl,
    internalUrl = nodeOptions.InternalUrl,
    environment = nodeOptions.Environment,
    peers = nodeOptions.Peers.Select(peer => new
    {
        nodeId = peer.NodeId,
        url = peer.Url,
    }),
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    using var scope = startupLogger.BeginScope(new Dictionary<string, object>
    {
        ["NodeId"] = nodeOptions.NodeId,
        ["CorrelationId"] = string.Empty,
        ["EventName"] = "NodeStarted",
    });

    NodeLog.Started(startupLogger, nodeOptions.NodeId, nodeOptions.Peers.Count);
});

app.Run();

public partial class Program;
