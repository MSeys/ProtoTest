namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>Context supplied to an HTTP-based authenticator when a request is sent.</summary>
public sealed record ProtoHttpAuthenticationContext(
    HttpRequestMessage Request,
    ProtoExecutionContext Test,
    string ClientName);

/// <summary>Authenticates an outgoing HTTP request for a REST, GraphQL, or other HTTP-based client.</summary>
public interface IProtoHttpAuthenticator
{
    ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies several authenticators in order, tracing each application under a shared source.</summary>
public sealed class ProtoCompositeHttpAuthenticator(
    IEnumerable<IProtoHttpAuthenticator> authenticators,
    string traceSource) : IProtoHttpAuthenticator
{
    private readonly IReadOnlyList<IProtoHttpAuthenticator> _authenticators = [.. authenticators];

    public async ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var authenticator in _authenticators)
        {
            await context.Test.Trace
                .Operation("auth.handler.apply", $"Apply · {authenticator.GetType().Name}", traceSource)
                .With("auth.type", authenticator.GetType().FullName)
                .With("client.name", context.ClientName)
                .RunAsync(() => authenticator.AuthenticateAsync(context, cancellationToken));
        }
    }
}

/// <summary>
/// Declares, via attribute metadata, an authenticator that an HTTP protocol's lifecycle hook applies
/// to its named client. <see cref="Protocols"/> keeps protocol hooks from picking up each other's
/// authenticators when a test needs different authentication per protocol.
/// </summary>
public interface IProtoHttpAuthMetadata
{
    int Order { get; }

    IReadOnlyList<string> Protocols { get; }

    /// <summary>Returns whether this authenticator applies to the named protocol.</summary>
    bool AppliesTo(string protocolName)
        => Protocols.Count == 0
        || Protocols.Contains(protocolName, StringComparer.OrdinalIgnoreCase);

    IProtoHttpAuthenticator Create(ProtoExecutionContext context);
}
