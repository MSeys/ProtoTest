namespace ProtoTest.GraphQL.Internal;

using System.Reflection;
using ProtoTest.Core;

internal sealed class GraphQLContextState : IProtoContext
{
    public string ClientName { get; init; } = "Default";
    public Func<ProtoExecutionContext, IGraphQLAuthenticator>? AuthenticatorFactory { get; init; }
    private int _requestSequence;
    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}

internal sealed class GraphQLLifecycleHook : IProtoTestHook
{
    public int Order => 100;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var method = context.TestMethod;
        var attribute = method.GetCustomAttribute<GraphQLClientAttribute>()
            ?? method.DeclaringType?.GetCustomAttribute<GraphQLClientAttribute>();
        var methodAuth = method.GetCustomAttributes(inherit: true).OfType<IGraphQLAuthMetadata>().ToArray();
        var auth = methodAuth.Length > 0
            ? methodAuth
            : method.DeclaringType?.GetCustomAttributes(inherit: true).OfType<IGraphQLAuthMetadata>().ToArray() ?? [];
        context.SetContext(new GraphQLContextState
        {
            ClientName = attribute?.ClientName ?? "Default",
            AuthenticatorFactory = auth.Length == 0 ? null : test =>
            {
                var authenticators = auth.OrderBy(item => item.Order).Select(item => item.Create(test)).ToArray();
                return authenticators.Length == 1 ? authenticators[0] : new CompositeGraphQLAuthenticator(authenticators);
            }
        });
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
