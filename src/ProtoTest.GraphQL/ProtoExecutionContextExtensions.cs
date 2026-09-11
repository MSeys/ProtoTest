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
        return new GraphQLRequestBuilder(client, context, selectedName)
            .UseAuthenticatorFactory(context.TryContext<GraphQLContextState>()?.AuthenticatorFactory)
            .UseBaseAddressResolver(alias?.ResolveEndpointAsync ?? registration?.ResolveAsync);
    }
}
