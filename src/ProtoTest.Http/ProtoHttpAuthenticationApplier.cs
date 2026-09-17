namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Resolves and applies (or explicitly skips) authentication for one outgoing HTTP request, shared by
/// every protocol request builder so the resolve/trace/apply sequence is written once.
/// </summary>
public static class ProtoHttpAuthenticationApplier
{
    /// <summary>
    /// Resolves <paramref name="authenticator"/> from <paramref name="factory"/> when not already
    /// resolved, applies it, and returns the resolved authenticator so the caller can cache it for
    /// later requests built from the same fluent builder. Traces an "auth.skip" event instead of
    /// "auth.apply" when no factory is configured.
    /// </summary>
    public static async ValueTask<IProtoHttpAuthenticator?> ApplyAsync(
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? factory,
        IProtoHttpAuthenticator? authenticator,
        HttpRequestMessage request,
        ProtoExecutionContext context,
        string clientName,
        string protocolLabel,
        string traceSource,
        CancellationToken cancellationToken)
    {
        if (factory is null)
        {
            context.Trace.WriteEvent(
                "auth.skip",
                "Authentication · None",
                traceSource,
                outcome: ProtoTraceOutcome.Succeeded,
                attributes: new Dictionary<string, string?> { ["client.name"] = clientName });
            return authenticator;
        }

        authenticator ??= factory(context)
            ?? throw new InvalidOperationException("The authenticator factory returned null.");

        await context.Trace
            .Operation("auth.apply", $"Apply {protocolLabel} authentication", traceSource)
            .With("client.name", clientName)
            .With("auth.type", authenticator.GetType().FullName)
            .RunAsync(() => authenticator.AuthenticateAsync(
                new ProtoHttpAuthenticationContext(request, context, clientName), cancellationToken));
        return authenticator;
    }
}
