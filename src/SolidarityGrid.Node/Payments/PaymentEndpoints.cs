using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SolidarityGrid.Application.Payments;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Configuration;

namespace SolidarityGrid.Node.Payments;

public static class PaymentEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyReplayedHeaderName = "Idempotency-Replayed";

    public static IEndpointRouteBuilder MapPaymentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/pay", SubmitAsync);
        endpoints.MapGet("/payments/{paymentId:guid}", GetByIdAsync);
        return endpoints;
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext context,
        SubmitPaymentRequest request,
        SubmitPaymentUseCase useCase,
        IOptions<NodeOptions> nodeOptions,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(context.Request.Headers, out var idempotencyKey))
        {
            return PaymentProblemDetails.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid payment request",
                "The Idempotency-Key header is required.",
                PaymentErrorCodes.IdempotencyKeyRequired);
        }

        var result = await useCase.ExecuteAsync(
            new SubmitPaymentCommand(
                idempotencyKey,
                request.Amount,
                request.Currency ?? string.Empty),
            GetCorrelationId(context),
            cancellationToken);

        return result.Outcome switch
        {
            SubmitPaymentOutcome.Created => CreateSuccess(
                context,
                result.Payment!,
                nodeOptions.Value.NodeId,
                isReplay: false),
            SubmitPaymentOutcome.Replayed => CreateSuccess(
                context,
                result.Payment!,
                nodeOptions.Value.NodeId,
                isReplay: true),
            SubmitPaymentOutcome.ReplicationUnavailable =>
                CreateReplicationUnavailable(context, result),
            SubmitPaymentOutcome.Conflict => PaymentProblemDetails.Create(
                context,
                StatusCodes.Status409Conflict,
                "Idempotency key conflict",
                result.ErrorMessage ?? "The idempotency key conflicts with the request.",
                result.ErrorCode!),
            SubmitPaymentOutcome.Invalid => PaymentProblemDetails.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid payment request",
                result.ErrorMessage ?? "The payment request is invalid.",
                result.ErrorCode!),
            _ => throw new InvalidOperationException("Unknown submit payment outcome."),
        };
    }

    private static IResult CreateReplicationUnavailable(
        HttpContext context,
        SubmitPaymentResult result)
    {
        context.Response.Headers.RetryAfter = "1";
        context.Response.Headers.Location =
            $"/payments/{result.Payment!.Id:D}";
        return PaymentProblemDetails.Create(
            context,
            StatusCodes.Status503ServiceUnavailable,
            "Payment replication unavailable",
            "A durable payment quorum is not currently available. Retry the same request.",
            result.ErrorCode!);
    }

    private static async Task<IResult> GetByIdAsync(
        HttpContext context,
        Guid paymentId,
        GetPaymentByIdUseCase useCase,
        CancellationToken cancellationToken)
    {
        var payment = await useCase.ExecuteAsync(paymentId, cancellationToken);
        return payment is null
            ? PaymentProblemDetails.Create(
                context,
                StatusCodes.Status404NotFound,
                "Payment not found",
                "The requested payment does not exist on this node.",
                PaymentApplicationErrorCodes.PaymentNotFound)
            : Results.Ok(PaymentHttpMapper.ToResponse(payment));
    }

    private static IResult CreateSuccess(
        HttpContext context,
        PaymentDto payment,
        string nodeId,
        bool isReplay)
    {
        var location = $"/payments/{payment.Id:D}";
        context.Response.Headers.Location = location;
        context.Response.Headers[IdempotencyReplayedHeaderName] =
            isReplay ? "true" : "false";
        var response = PaymentHttpMapper.ToSubmitResponse(
            payment,
            nodeId,
            isReplay);

        return isReplay
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status202Accepted);
    }

    private static bool TryReadIdempotencyKey(
        IHeaderDictionary headers,
        out string idempotencyKey)
    {
        idempotencyKey = string.Empty;
        if (!headers.TryGetValue(
                IdempotencyKeyHeaderName,
                out StringValues values) ||
            values.Count != 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }

        idempotencyKey = values[0]!;
        return true;
    }

    private static string GetCorrelationId(HttpContext context) =>
        context.Items[Diagnostics.CorrelationIdMiddleware.ItemName]?.ToString() ??
        context.TraceIdentifier;
}
