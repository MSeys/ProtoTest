namespace ProtoTest.AspNetCore.Internal;

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using ProtoTest.Core;

/// <summary>
/// Injects the ambient W3C trace context into in-process test server requests. A real HTTP handler
/// propagates trace context itself, but the TestServer handler bypasses the HTTP diagnostics, so
/// ProtoTest adds the header to keep application spans in the same trace as the test that caused them.
/// It also carries the test's id, which is how the application's <see cref="TimeProvider"/> finds the
/// test's clock on a request flow that no longer has the test's ambient context.
/// </summary>
internal sealed class ProtoTraceContextHandler : DelegatingHandler
{
    internal const string TestIdHeader = "x-prototest-test";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ApplyTo(request);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Adds the ambient trace context and, when a test is active on this flow, that test's id to an
    /// outbound request. The handler applies this to every in-process HTTP client request; a hosting
    /// integration that opens a raw request applies it itself, so the application resolves the same
    /// test the HTTP path carries.
    /// </summary>
    internal static void ApplyTo(HttpRequestMessage request)
    {
        if (Activity.Current is { } activity && !request.Headers.Contains("traceparent"))
        {
            request.Headers.TryAddWithoutValidation(
                "traceparent",
                $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{(activity.Recorded ? "01" : "00")}");
        }

        if (ProtoHost.CurrentContextOrNull is { } context && !request.Headers.Contains(TestIdHeader))
        {
            request.Headers.TryAddWithoutValidation(TestIdHeader, context.TestId);
        }
    }

    /// <summary>
    /// Adds the same context to the request the test server hands the application, for an in-process
    /// client that opens one raw - the WebSocket handshake configured through <c>ConfigureRequest</c>.
    /// </summary>
    internal static void ApplyTo(HttpRequest request)
    {
        if (Activity.Current is { } activity && !request.Headers.ContainsKey("traceparent"))
        {
            request.Headers["traceparent"] =
                $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{(activity.Recorded ? "01" : "00")}";
        }

        if (ProtoHost.CurrentContextOrNull is { } context && !request.Headers.ContainsKey(TestIdHeader))
        {
            request.Headers[TestIdHeader] = context.TestId;
        }
    }
}
