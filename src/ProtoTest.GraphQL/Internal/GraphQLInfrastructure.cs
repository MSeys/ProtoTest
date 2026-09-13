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
        context.Trace.WriteEvent(
            "graphql.context.configure",
            $"GraphQL context · {attribute?.ClientName ?? "Default"}",
            "ProtoTest.GraphQL",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            new Dictionary<string, string?>
            {
                ["client.name"] = attribute?.ClientName ?? "Default",
                ["auth.source"] = methodAuth.Length > 0 ? "method" : auth.Length > 0 ? "class" : "none",
                ["auth.count"] = auth.Length.ToString(),
                ["auth.types"] = string.Join(", ", auth.Select(AuthTypeName))
            });
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    private static string AuthTypeName(IGraphQLAuthMetadata metadata)
        => metadata.GetType().GenericTypeArguments.FirstOrDefault()?.FullName
           ?? metadata.GetType().FullName
           ?? metadata.GetType().Name;
}
