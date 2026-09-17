namespace ProtoTest.GraphQL;

using System.Net.WebSockets;

/// <summary>Selects the wire transport used for GraphQL subscriptions.</summary>
public enum GraphQLSubscriptionTransport
{
    WebSocket,
    Sse
}

/// <summary>
/// Creates connected WebSockets for GraphQL subscriptions. Implementations must
/// negotiate the <c>graphql-transport-ws</c> subprotocol.
/// </summary>
public interface IGraphQLWebSocketFactory
{
    ValueTask<WebSocket> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);
}

internal sealed class ClientGraphQLWebSocketFactory : IGraphQLWebSocketFactory
{
    public async ValueTask<WebSocket> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.AddSubProtocol("graphql-transport-ws");
            foreach (var header in headers)
                socket.Options.SetRequestHeader(header.Key, header.Value);
            await socket.ConnectAsync(endpoint, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal sealed record GraphQLSubscriptionTransportRegistration(
    string TargetName,
    GraphQLSubscriptionTransport Transport);
