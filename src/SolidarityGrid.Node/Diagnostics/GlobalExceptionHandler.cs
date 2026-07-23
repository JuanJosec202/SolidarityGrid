using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SolidarityGrid.Node.Configuration;

namespace SolidarityGrid.Node.Diagnostics;

public sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService,
    IOptions<NodeOptions> nodeOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString();

        LogUnexpectedError(
            logger,
            nodeOptions.Value.NodeId,
            correlationId,
            "UnhandledException",
            exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "The request could not be completed.",
            },
            Exception = exception,
        });
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Unexpected error on node {NodeId}. CorrelationId {CorrelationId}. EventName {EventName}.")]
    private static partial void LogUnexpectedError(
        ILogger logger,
        string nodeId,
        string? correlationId,
        string eventName,
        Exception exception);
}
