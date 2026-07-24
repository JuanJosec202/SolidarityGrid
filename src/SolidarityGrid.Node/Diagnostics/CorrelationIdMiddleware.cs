using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SolidarityGrid.Node.Configuration;

namespace SolidarityGrid.Node.Diagnostics;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemName = "CorrelationId";
    private const int MaximumLength = 128;

    public async Task InvokeAsync(HttpContext context, IOptions<NodeOptions> nodeOptions)
    {
        var correlationId = GetCorrelationId(context.Request.Headers);
        context.Items[ItemName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["NodeId"] = nodeOptions.Value.NodeId,
            ["CorrelationId"] = correlationId,
            ["EventName"] = "HttpRequest",
        });

        await next(context);
    }

    private static string GetCorrelationId(IHeaderDictionary headers)
    {
        if (headers.TryGetValue(HeaderName, out StringValues values))
        {
            var supplied = values.FirstOrDefault();
            if (supplied is not null && IsValid(supplied))
            {
                return supplied;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } &&
        value.All(IsSafeAsciiCharacter);

    private static bool IsSafeAsciiCharacter(char value) =>
        value is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            '-' or
            '.' or
            '_' or
            ':';
}
