namespace ProtoTest.AspNetCore.Internal;

using System.Diagnostics;
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

        return base.SendAsync(request, cancellationToken);
    }
}
