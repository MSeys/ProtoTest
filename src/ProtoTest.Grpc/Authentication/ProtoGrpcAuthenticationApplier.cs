namespace ProtoTest.Grpc.Authentication;

using global::Grpc.Core;
using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Applies the test's authenticators to one outgoing call's metadata. gRPC stays on the shared
/// <c>[Auth]</c> pipeline: authenticators keep writing HTTP headers, and the applier translates them to
/// lowercase metadata keys, which is exactly what gRPC metadata is.
/// </summary>
internal static class ProtoGrpcAuthenticationApplier
{
    public static async ValueTask ApplyAsync(
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? factory,
        Metadata metadata,
        ProtoExecutionContext context,
        string clientName,
        ProtoTraceOperation? callOperation,
        CancellationToken cancellationToken)
    {
        if (factory is null)
        {
            callOperation?.SetAttribute("auth.outcome", "skipped");
            return;
        }

        var authenticator = factory(context)
            ?? throw new InvalidOperationException("The authenticator factory returned null.");
        callOperation?
            .SetAttribute("auth.outcome", "applied")
            .SetAttribute("auth.type", authenticator.GetType().FullName);

        using var request = new HttpRequestMessage();
        await authenticator.AuthenticateAsync(
            new ProtoHttpAuthenticationContext(request, context, clientName),
            cancellationToken);
        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                metadata.Add(header.Key.ToLowerInvariant(), value);
            }
        }
    }
}
