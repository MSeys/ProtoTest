namespace ProtoTest.GraphQL.Internal;

using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Per-test GraphQL state. Kept protocol-local (rather than shared with REST) because both protocol
/// lifecycle hooks run for every test and would otherwise overwrite each other's authenticator.
/// </summary>
internal sealed class GraphQLContextState : IProtoContext
{
    private int _requestSequence;

    public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; init; }

    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}

/// <summary>Resolves <c>[GraphQLAuth]</c> authenticators for the test; the client comes from <c>[Application]</c>.</summary>
internal sealed class GraphQLLifecycleHook()
    : ProtoHttpAuthLifecycleHook<IGraphQLAuthMetadata>("GraphQL")
{
    protected override void SetContext(
        ProtoExecutionContext context,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
        => context.SetContext(new GraphQLContextState
        {
            AuthenticatorFactory = authenticatorFactory
        });
}
