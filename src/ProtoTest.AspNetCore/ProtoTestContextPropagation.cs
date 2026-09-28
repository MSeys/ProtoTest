namespace ProtoTest.AspNetCore;

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using ProtoTest.Core;

/// <summary>
/// Propagates the test's context onto a request an in-process transport opens itself. A real HTTP
/// handler propagates the W3C trace context itself, but the <c>TestServer</c> handler bypasses the HTTP
/// diagnostics, so an in-process request carries the ambient trace context explicitly; it also carries
/// the test's id, which is how the application's <see cref="TimeProvider"/> finds the test's clock on a
/// request flow that no longer has the test's ambient context.
/// </summary>
public static class ProtoTestContextPropagation
{
    /// <summary>
    /// The header that carries the test id to the application's clock bridge. The in-process HTTP
    /// client's handler sends it on every request; a transport that opens a raw request applies
    /// <see cref="ApplyTo(HttpRequest)"/> so the application resolves the same test.
    /// </summary>
    public const string TestIdHeader = "x-prototest-test";

    /// <summary>
    /// Adds the ambient trace context and, when a test is active on this flow, that test's id to an
    /// outbound request. An existing header is left untouched.
    /// </summary>
    public static void ApplyTo(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Activity.Current is { } activity && !request.Headers.Contains("traceparent"))
        {
            request.Headers.TryAddWithoutValidation("traceparent", TraceParent(activity));
        }

        if (ProtoHost.CurrentContextOrNull is { } context && !request.Headers.Contains(TestIdHeader))
        {
            request.Headers.TryAddWithoutValidation(TestIdHeader, context.TestId);
        }
    }

    /// <summary>
    /// Adds the same context to the request the test server hands the application, for an in-process
    /// client that opens one raw - a WebSocket handshake configured through <c>ConfigureRequest</c>.
    /// </summary>
    public static void ApplyTo(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Activity.Current is { } activity && !request.Headers.ContainsKey("traceparent"))
        {
            request.Headers["traceparent"] = TraceParent(activity);
        }

        if (ProtoHost.CurrentContextOrNull is { } context && !request.Headers.ContainsKey(TestIdHeader))
        {
            request.Headers[TestIdHeader] = context.TestId;
        }
    }

    private static string TraceParent(Activity activity)
        => $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{(activity.Recorded ? "01" : "00")}";
}
