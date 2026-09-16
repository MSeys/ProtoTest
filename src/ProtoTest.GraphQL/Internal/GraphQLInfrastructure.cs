namespace ProtoTest.GraphQL.Internal;

using ProtoTest.Core;
using ProtoTest.Http;

internal sealed class GraphQLContextState : IProtoContext
{
    public string ClientName { get; init; } = "Default";
    public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; init; }
    private int _requestSequence;
    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}

internal sealed class GraphQLLifecycleHook()
    : ProtoHttpAuthLifecycleHook<GraphQLClientAttribute, IGraphQLAuthMetadata>("GraphQL")
{
    protected override string GetClientName(GraphQLClientAttribute? attribute) => attribute?.ClientName ?? "Default";

    protected override void SetContext(
        ProtoExecutionContext context,
        string clientName,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
        => context.SetContext(new GraphQLContextState
        {
            ClientName = clientName,
            AuthenticatorFactory = authenticatorFactory
        });
}
