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
        var application = ProtoApplicationResolution.ResolveApplicationName(context);

        var selected = clientName ?? ProtoApplicationResolution.ResolveClientName(context, "GraphQL", fallback: "Default");
        var resolvedName = application is null ? selected : $"{application}:{selected}";

        var registration = context.Services.GetServices<ProtoHttpBaseAddressRegistration>()
            .LastOrDefault(item => string.Equals(item.ProtocolName, "GraphQL", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(item.ClientName, resolvedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ClientName, selected, StringComparison.OrdinalIgnoreCase)));
        var alias = context.Services.GetServices<ProtoHttpClientAliasRegistration>()
            .LastOrDefault(item => string.Equals(item.ProtocolName, "GraphQL", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(item.ClientName, resolvedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ClientName, selected, StringComparison.OrdinalIgnoreCase)));

        var client = context.TryClient<HttpClient>(alias?.SourceClientName ?? resolvedName);
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? transportResolver = null;
        if (client is null || client.BaseAddress is null)
        {
            // No endpoint configured for this client: fall back to the application's in-process transport,
            // rooted at the GraphQL endpoint path.
            var transport = ProtoApplicationResolution.ResolveTransportClient(context, application);
            if (transport is not null)
            {
                client = transport;
                var endpointPath = application is null
                    ? null
                    : ProtoApplication.Endpoint(context.Configuration, application, "GraphQL");
                if (!string.IsNullOrWhiteSpace(endpointPath))
                {
                    transportResolver = (_, _) => ValueTask.FromResult(new Uri(transport.BaseAddress!, endpointPath));
                }
            }
        }

        if (client is null)
        {
            throw new InvalidOperationException(
                $"No HTTP client '{resolvedName}' is registered. Register it under the application, back the " +
                $"application with AddAspNetCoreServer, or set 'ProtoTest:Applications:{application}:BaseUrl'.");
        }

        var authenticatorFactory = state?.AuthenticatorFactory;
        var transportRegistration = context.Services.GetServices<GraphQLSubscriptionTransportRegistration>()
            .LastOrDefault(item => string.Equals(item.TargetName, resolvedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.TargetName, selected, StringComparison.OrdinalIgnoreCase));

        var configuration = context.Services.GetService<IConfiguration>();
        var configuredTransport = configuration is null
            ? null
            : ProtoApplication.Section(configuration, application ?? selected)["GraphQL:SubscriptionTransport"];
        var subscriptionTransport = transportRegistration?.Transport
            ?? ParseSubscriptionTransport(configuredTransport, selected);

        context.Trace.WriteEvent(
            "graphql.builder.create",
            $"GraphQL builder · {selected}",
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["client.name"] = selected,
                ["application.name"] = application,
                ["client.source_name"] = alias?.SourceClientName ?? resolvedName,
                ["auth.configured"] = (authenticatorFactory is not null).ToString().ToLowerInvariant(),
                ["endpoint.resolver"] = alias is not null ? "alias" : registration is not null ? "per-test" : "client"
            });

        // The builder works with the registered target name: it is the identity clients, observations,
        // and collectors agree on. The selected name is only how the caller addressed the client.
        return new GraphQLRequestBuilder(client, context, resolvedName)
            .UseAuthenticatorFactory(authenticatorFactory)
            .UseBaseAddressResolver(transportResolver ?? alias?.ResolveEndpointAsync ?? registration?.ResolveAsync)
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
