using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SolidarityGrid.Application.Abstractions.Persistence;
using SolidarityGrid.Application.Payments;
using SolidarityGrid.Domain.Payments;
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
        if (exception is OperationCanceledException &&
            httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString();
        var (status, title, detail, code) = exception switch
        {
            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                "Invalid payment request",
                "The request body is missing or contains invalid JSON.",
                PaymentApplicationErrorCodes.PaymentInvalidRequest),
            PaymentDomainException domainException => (
                StatusCodes.Status400BadRequest,
                "Invalid payment request",
                domainException.Message,
                domainException.Code),
            PaymentPersistenceException => (
                StatusCodes.Status503ServiceUnavailable,
                "Payment storage unavailable",
                "The payment could not be persisted at this time.",
                "PAYMENT_STORAGE_UNAVAILABLE"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "The request could not be completed.",
                "UNEXPECTED_ERROR"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnexpectedError(
                logger,
                nodeOptions.Value.NodeId,
                correlationId,
                "UnhandledException",
                exception);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Type = $"https://solidaritygrid.dev/problems/{code.ToLowerInvariant()}",
                Status = status,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path,
                Extensions =
                {
                    ["code"] = code,
                    ["correlationId"] = correlationId,
                },
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
