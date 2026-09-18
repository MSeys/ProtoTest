namespace ProtoTest.AspNetCore.Internal;

using System.Diagnostics;

/// <summary>
/// Injects the ambient W3C trace context into in-process test server requests. A real HTTP handler
/// propagates trace context itself, but the TestServer handler bypasses the HTTP diagnostics, so
/// ProtoTest adds the header to keep application spans in the same trace as the test that caused them.
/// </summary>
internal sealed class ProtoTraceContextHandler : DelegatingHandler
{
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

        return base.SendAsync(request, cancellationToken);
    }
}
