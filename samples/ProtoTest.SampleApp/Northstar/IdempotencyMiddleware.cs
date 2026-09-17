namespace ProtoTest.SampleApp.Northstar;

using System.Collections.Concurrent;

/// <summary>
/// Replays the original response for repeated <c>POST /api/v1</c> requests that carry the same
/// <c>Idempotency-Key</c>, scoped to the calling token.
/// </summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<string, StoredResponse> Cache = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryKey(context, out var key))
        {
            await next(context);
            return;
        }

        if (Cache.TryGetValue(key, out var stored))
        {
            context.Response.StatusCode = stored.StatusCode;
            context.Response.ContentType = stored.ContentType;
            context.Response.Headers["Idempotency-Replayed"] = "true";
            await context.Response.WriteAsync(stored.Body);
            return;
        }

        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
            buffer.Position = 0;
            var body = await new StreamReader(buffer).ReadToEndAsync();
            context.Response.Body = original;
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                Cache[key] = new StoredResponse(context.Response.StatusCode, context.Response.ContentType, body);
            }

            await context.Response.WriteAsync(body);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private static bool TryKey(HttpContext context, out string key)
    {
        key = string.Empty;
        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Path.StartsWithSegments("/api/v1")
            || !context.Request.Headers.TryGetValue("Idempotency-Key", out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        key = $"{context.Request.Headers.Authorization}|{value}";
        return true;
    }

    private sealed record StoredResponse(int StatusCode, string? ContentType, string Body);
}
