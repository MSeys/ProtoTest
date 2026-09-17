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
/// Declares, via attribute metadata, an authenticator that a protocol's lifecycle hook applies to its
/// named client.
/// </summary>
public interface IProtoHttpAuthMetadata
{
    int Order { get; }
    IProtoHttpAuthenticator Create(ProtoExecutionContext context);
}

/// <summary>
/// Base for a protocol-specific generic <c>[XAuth&lt;TAuthenticator&gt;]</c> attribute. A concrete
/// protocol keeps its own sealed attribute (and a private marker interface) so that a test class
/// needing different authentication per client - for example a REST client and a GraphQL client on
/// the same class - can apply one attribute per protocol without either lifecycle hook picking up the
/// other's authenticator.
/// </summary>
public abstract class ProtoHttpAuthAttribute<TAuthenticator>(params object[] constructorArgs)
    : Attribute, IProtoHttpAuthMetadata
    where TAuthenticator : class, IProtoHttpAuthenticator
{
    public int Order { get; init; }

    IProtoHttpAuthenticator IProtoHttpAuthMetadata.Create(ProtoExecutionContext context)
        => ProtoAuthenticatorFactory.Create<TAuthenticator>(context, constructorArgs);
}
