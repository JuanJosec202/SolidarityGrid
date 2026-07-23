using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SolidarityGrid.Infrastructure;
using SolidarityGrid.Infrastructure.Mesh.Configuration;
using SolidarityGrid.Application;
using SolidarityGrid.Infrastructure.Persistence.Initialization;
using SolidarityGrid.Node.Configuration;
using SolidarityGrid.Node.Diagnostics;
using SolidarityGrid.Node.Health;
using SolidarityGrid.Node.Mesh;
using SolidarityGrid.Node.Payments;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080, listenOptions =>
        listenOptions.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(8081, listenOptions =>
        listenOptions.Protocols = HttpProtocols.Http2);
});
builder.Services
    .AddOptions<NodeOptions>()
    .Bind(builder.Configuration.GetRequiredSection(NodeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<NodeOptions>, NodeOptionsValidator>();
builder.Services.AddApplication();
builder.Services.AddNodeMesh();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddGrpc();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
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
_ = app.Services.GetRequiredService<IOptions<MeshTransportOptions>>().Value;
_ = app.Services.GetRequiredService<SolidarityGrid.Application.Mesh.IMeshNodeIdentity>();
_ = app.Services.GetRequiredService<SolidarityGrid.Application.Mesh.IMeshPeerDirectory>();
var timeProvider = app.Services.GetRequiredService<TimeProvider>();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

await using (var initializationScope = app.Services.CreateAsyncScope())
{
    var databaseInitializer = initializationScope.ServiceProvider
        .GetRequiredService<IDatabaseInitializer>();
    await databaseInitializer.InitializeAsync(app.Lifetime.ApplicationStopping);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort == 8081 &&
        (context.Request.ContentType is null ||
         !context.Request.ContentType.StartsWith(
             "application/grpc",
             StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

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
        meshPeers = "/mesh/peers",
        submitPayment = "/pay",
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
app.MapPaymentEndpoints();
app.MapMeshEndpoints();
var meshGrpcEndpoint = app.MapGrpcService<MeshControlGrpcService>();
meshGrpcEndpoint.RequireHost("*:8081", "localhost");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = (context, report) =>
        HealthResponseWriter.WriteAsync(
            context,
            report,
            nodeOptions.NodeId,
            timeProvider),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = (context, report) =>
        HealthResponseWriter.WriteAsync(
            context,
            report,
            nodeOptions.NodeId,
            timeProvider),
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
