namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Resolves and applies (or explicitly skips) authentication for one outgoing HTTP request, shared by
/// every protocol request builder. The outcome is an attribute of the request operation the caller owns,
/// not a separate entry: authentication is part of the request, not a step beside it.
/// </summary>
public static class ProtoHttpAuthenticationApplier
{
    /// <summary>
    /// Resolves <paramref name="authenticator"/> from <paramref name="factory"/> when not already
    /// resolved, applies it, and returns the resolved authenticator so the caller can cache it for
    /// later requests built from the same fluent builder. Sets <c>auth.outcome</c> and <c>auth.type</c>
    /// on <paramref name="requestOperation"/>.
    /// </summary>
    public static async ValueTask<IProtoHttpAuthenticator?> ApplyAsync(
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? factory,
        IProtoHttpAuthenticator? authenticator,
        HttpRequestMessage request,
        ProtoExecutionContext context,
        string clientName,
        ProtoTraceOperation? requestOperation,
        CancellationToken cancellationToken)
    {
        if (factory is null)
        {
            requestOperation?.SetAttribute("auth.outcome", "skipped");
            return authenticator;
        }

        authenticator ??= factory(context)
            ?? throw new InvalidOperationException("The authenticator factory returned null.");

        requestOperation?
            .SetAttribute("auth.outcome", "applied")
            .SetAttribute("auth.type", authenticator.GetType().FullName);
        await authenticator.AuthenticateAsync(
            new ProtoHttpAuthenticationContext(request, context, clientName), cancellationToken);
        return authenticator;
    }
}
