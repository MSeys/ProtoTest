namespace ProtoTest.AspNetCore.Internal;

/// <summary>
/// Injects the ambient trace context and the active test's id into in-process test server requests,
/// through the public <see cref="ProtoTestContextPropagation"/> rule a hand-written transport applies
/// as well.
/// </summary>
internal sealed class ProtoTraceContextHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ProtoTestContextPropagation.ApplyTo(request);
        return base.SendAsync(request, cancellationToken);
    }
}
