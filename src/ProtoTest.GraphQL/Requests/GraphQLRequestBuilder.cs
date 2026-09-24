namespace ProtoTest.GraphQL;

using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed partial class GraphQLRequestBuilder
    : ProtoHttpRequestBuilder<GraphQLResponse, GraphQLRequestBuilder>
{
    private static readonly MediaTypeWithQualityHeaderValue GraphQLMediaType = new("application/graphql-response+json");
    private GraphQLBuiltOperation? _operation;
    private object? _variables;
    private ShapePlan? _shapePlan;
    private GraphQLSubscriptionTransport _subscriptionTransport = GraphQLSubscriptionTransport.WebSocket;
    private object? _connectionPayload;

    internal GraphQLRequestBuilder(HttpClient client, ProtoExecutionContext context, string targetName, string? clientEntityName = null)
        : base(client, context, targetName, ProtoGraphQLBuilder.Protocol, clientEntityName)
    {
    }


    protected override void OnAuthenticationConfigured(string source, Type? authenticatorType)
    {
        var attributes = new Dictionary<string, string?> { ["auth.source"] = source };
        if (authenticatorType is not null)
        {
            attributes["auth.type"] = authenticatorType.FullName;
        }

        TraceConfiguration(
            authenticatorType is null ? "auth.disable" : "auth.select",
            authenticatorType is null ? "Authentication · Disabled" : $"Authentication · {authenticatorType.Name}",
            attributes);
    }

    protected override void OnHeaderConfigured(string name, bool isNewHeader)
    {
        if (!isNewHeader)
        {
            return;
        }

        TraceConfiguration("http.header.configure", $"Header · {name}", new Dictionary<string, string?>
        {
            ["http.header.name"] = name,
            ["http.header.value_recorded"] = "false"
        });
    }

    public GraphQLRequestBuilder Variables(object variables)
    {
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        TraceConfiguration("graphql.variables.configure", "GraphQL variables configured", new Dictionary<string, string?>()
        {
            ["variables.type"] = variables.GetType().FullName
        });
        return this;
    }

    /// <summary>Sets the optional graphql-transport-ws connection_init payload.</summary>
    public GraphQLRequestBuilder ConnectionPayload(object payload)
    {
        _connectionPayload = payload ?? throw new ArgumentNullException(nameof(payload));
        TraceConfiguration(
            "graphql.subscription.connection_payload.configure",
            "GraphQL subscription connection payload configured",
            new Dictionary<string, string?> { ["payload.type"] = payload.GetType().FullName });
        return this;
    }

    internal GraphQLRequestBuilder UseSubscriptionTransport(GraphQLSubscriptionTransport transport)
    {
        _subscriptionTransport = transport;
        return this;
    }

}
