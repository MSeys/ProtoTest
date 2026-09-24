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
{
    public async Task<GraphQLSubscription> SubscribeAsync(CancellationToken cancellationToken = default)
    {
        var operation = _operation
            ?? throw new InvalidOperationException("Configure a subscription before subscribing.");
        if (operation.Kind != GraphQLOperationKind.Subscription)
            throw new InvalidOperationException("SubscribeAsync requires a subscription operation.");

        var identifier = $"subscription {operation.Name ?? "<anonymous>"}";
        var stopwatch = Stopwatch.StartNew();
        var endpoint = await ProtoHttpEndpoint.ResolveBaseAddressAsync(
            Client,
            ProtoGraphQLBuilder.ProtocolName,
            TargetName,
            BaseAddressResolver,
            Context,
            cancellationToken);

        HttpRequestMessage? request = null;
        HttpResponseMessage? rawResponse = null;
        WebSocket? rawSocket = null;
        try
        {
            var prepared = await PrepareRequestAsync(
                operation,
                identifier,
                endpoint,
                authOperation: null,
                subscription: true,
                cancellationToken);
            request = prepared.Request;
            var attachmentOptions = prepared.AttachmentOptions;
            var requestContent = prepared.Content;

            if (_subscriptionTransport == GraphQLSubscriptionTransport.WebSocket)
            {
                var webSocketEndpoint = ToWebSocketUri(endpoint);
                var headers = request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(", ", header.Value),
                    StringComparer.OrdinalIgnoreCase);
                rawSocket = await Context.Services.GetRequiredService<IGraphQLWebSocketFactory>()
                    .ConnectAsync(webSocketEndpoint, headers, cancellationToken);
                await GraphQLWebSocketProtocol.SendAsync(
                    rawSocket,
                    new { type = "connection_init", payload = _connectionPayload },
                    cancellationToken);
                await AwaitConnectionAcknowledgementAsync(rawSocket, cancellationToken);
                using var envelope = JsonDocument.Parse(requestContent.DiagnosticJson);
                await GraphQLWebSocketProtocol.SendAsync(
                    rawSocket,
                    new
                    {
                        id = "1",
                        type = "subscribe",
                        payload = envelope.RootElement.Clone()
                    },
                    cancellationToken);
                request.Dispose();
                TraceSubscriptionStarted(operation, "websocket", null);
                var webSocketSubscription = new GraphQLSubscription(
                    rawSocket,
                    ResolveResponseOptions().MaxResponseBodyBytes,
                    stopwatch,
                    Context,
                    TargetName,
                    identifier,
                    operation,
                    attachmentOptions,
                    prepared.AttachmentPrefix,
                    prepared.VariablesJson,
                    _shapePlan?.RootField);
                rawSocket = null;
                return webSocketSubscription;
            }

            rawResponse = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var stream = await rawResponse.Content.ReadAsStreamAsync(cancellationToken);
            request.Dispose();
            TraceSubscriptionStarted(operation, "sse", (int)rawResponse.StatusCode);
            var subscription = new GraphQLSubscription(
                rawResponse,
                stream,
                ResolveResponseOptions().MaxResponseBodyBytes,
                stopwatch,
                Context,
                TargetName,
                identifier,
                operation,
                attachmentOptions,
                prepared.AttachmentPrefix,
                prepared.VariablesJson,
                _shapePlan?.RootField);
            rawResponse = null;
            return subscription;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            request?.Dispose();
            rawResponse?.Dispose();
            rawSocket?.Dispose();
            TryRecordFailure(operation, identifier, endpoint, stopwatch.Elapsed, exception, cancellationToken);
            throw;
        }
    }
    private async Task AwaitConnectionAcknowledgementAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var maxBytes = ResolveResponseOptions().MaxResponseBodyBytes;
        while (true)
        {
            var message = await GraphQLWebSocketProtocol.ReceiveAsync(socket, maxBytes, cancellationToken)
                ?? throw new GraphQLProtocolException(
                    "The GraphQL WebSocket closed before acknowledging the connection.",
                    string.Empty);
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
            if (type == "connection_ack") return;
            if (type == "ping")
            {
                await GraphQLWebSocketProtocol.SendAsync(socket, new { type = "pong" }, cancellationToken);
                continue;
            }
            if (type is "connection_error" or "error")
                throw new GraphQLProtocolException(
                    "The GraphQL WebSocket rejected the connection.",
                    JsonDiagnosticSanitizer.Sanitize(
                        message,
                        Context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName)));
            throw new GraphQLProtocolException(
                $"Expected a GraphQL WebSocket 'connection_ack' message, but received '{type ?? "<missing>"}'.",
                JsonDiagnosticSanitizer.Sanitize(
                    message,
                    Context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName)));
        }
    }

    private void TraceSubscriptionStarted(GraphQLBuiltOperation operation, string transport, int? statusCode)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["client.name"] = TargetName,
            ["graphql.operation.name"] = operation.Name,
            ["graphql.transport"] = transport
        };
        if (statusCode is not null) attributes["http.response.status_code"] = statusCode.Value.ToString();
        Context.Trace.WriteEvent(
            "graphql.subscription.start",
            $"GraphQL subscription · {operation.Name ?? "<anonymous>"}",
            ProtoGraphQLBuilder.Protocol.TraceSource,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: attributes);
    }

    private static Uri ToWebSocketUri(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        if (endpoint.IsDefaultPort) builder.Port = -1;
        return builder.Uri;
    }

}
