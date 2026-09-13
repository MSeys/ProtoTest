namespace ProtoTest.GraphQL;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.GraphQL.Internal;
using Microsoft.Extensions.DependencyInjection;

public static class ProtoExecutionContextExtensions
{
    public static GraphQLRequestBuilder GraphQL(this ProtoExecutionContext context, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selectedName = clientName ?? context.TryContext<GraphQLContextState>()?.ClientName ?? "Default";
        var registration = context.Services.GetServices<ProtoHttpBaseAddressRegistration>()
            .LastOrDefault(item => string.Equals(item.ProtocolName, "GraphQL", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ClientName, selectedName, StringComparison.OrdinalIgnoreCase));
        var alias = context.Services.GetServices<ProtoHttpClientAliasRegistration>()
            .LastOrDefault(item => string.Equals(item.ProtocolName, "GraphQL", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ClientName, selectedName, StringComparison.OrdinalIgnoreCase));
        var client = context.Client<HttpClient>(alias?.SourceClientName ?? selectedName);
        var authenticatorFactory = context.TryContext<GraphQLContextState>()?.AuthenticatorFactory;
        context.Trace.WriteEvent(
            "graphql.builder.create",
            $"GraphQL builder · {selectedName}",
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["client.name"] = selectedName,
                ["client.source_name"] = alias?.SourceClientName ?? selectedName,
                ["auth.configured"] = (authenticatorFactory is not null).ToString().ToLowerInvariant(),
                ["endpoint.resolver"] = alias is not null ? "alias" : registration is not null ? "per-test" : "client"
            });
        return new GraphQLRequestBuilder(client, context, selectedName)
            .UseAuthenticatorFactory(authenticatorFactory)
            .UseBaseAddressResolver(alias?.ResolveEndpointAsync ?? registration?.ResolveAsync);
    }
}
