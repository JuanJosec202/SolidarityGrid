using SolidarityGrid.Node.Diagnostics;

namespace SolidarityGrid.Node.Payments;

public static class PaymentProblemDetails
{
    public static IResult Create(
        HttpContext context,
        int status,
        string title,
        string detail,
        string code)
    {
        var correlationId =
            context.Items[CorrelationIdMiddleware.ItemName]?.ToString() ??
            context.TraceIdentifier;

        return Results.Problem(
            type: $"https://solidaritygrid.dev/problems/{code.ToLowerInvariant()}",
            title: title,
            statusCode: status,
            detail: detail,
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = correlationId,
            });
    }
}
