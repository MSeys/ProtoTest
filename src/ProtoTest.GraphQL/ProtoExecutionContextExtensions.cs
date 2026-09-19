namespace ProtoTest.GraphQL;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.GraphQL.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for a GraphQL request. Inside an <c>[Application]</c> the default or bound GraphQL
    /// client is used unless <paramref name="clientName"/> names another.
    /// </summary>
    public static GraphQLRequestBuilder GraphQL(this ProtoExecutionContext context, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.TryResolve<GraphQLContextState>();
        var resolution = ProtoHttpClientResolver.Resolve(
            context, "GraphQL", clientName, defaultEndpointName: "GraphQL");

        var authenticatorFactory = state?.AuthenticatorFactory;
        var transportRegistration = context.Services.GetServices<GraphQLSubscriptionTransportRegistration>()
            .LastOrDefault(item => string.Equals(item.TargetName, resolution.ResolvedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TargetName, resolution.RequestedName, StringComparison.OrdinalIgnoreCase));

        var configuration = context.Services.GetService<IConfiguration>();
        var configuredTransport = configuration is null
            ? null
            : ProtoApplication.Section(
                configuration, resolution.ApplicationName ?? resolution.RequestedName)["GraphQL:SubscriptionTransport"];
        var subscriptionTransport = transportRegistration?.Transport
            ?? ParseSubscriptionTransport(configuredTransport, resolution.RequestedName);

        context.Trace.WriteEvent(
            "graphql.builder.create",
            $"GraphQL builder · {resolution.RequestedName}",
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["client.name"] = resolution.RequestedName,
                ["application.name"] = resolution.ApplicationName,
                ["client.source_name"] = resolution.SourceName,
                ["auth.configured"] = (authenticatorFactory is not null).ToString().ToLowerInvariant(),
                ["endpoint.resolver"] = resolution.EndpointResolver
            });

        // The builder works with the registered target name: it is the identity clients, observations,
        // and collectors agree on. The selected name is only how the caller addressed the client.
        return new GraphQLRequestBuilder(resolution.Client, context, resolution.ResolvedName)
            .UseAuthenticatorFactory(authenticatorFactory)
            .UseBaseAddressResolver(resolution.BaseAddressResolver)
            .UseSubscriptionTransport(subscriptionTransport);
    }

    private static GraphQLSubscriptionTransport ParseSubscriptionTransport(string? value, string clientName)
    {
        if (string.IsNullOrWhiteSpace(value)) return GraphQLSubscriptionTransport.WebSocket;
        if (Enum.TryParse<GraphQLSubscriptionTransport>(value, ignoreCase: true, out var transport)
            && Enum.IsDefined(transport))
            return transport;
        throw new InvalidOperationException(
            $"GraphQL subscription transport '{value}' for client '{clientName}' is invalid. Use 'WebSocket' or 'Sse'.");
    }
}
