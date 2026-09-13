namespace ProtoTest.GraphQL.Internal;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

internal static class GraphQLWebSocketProtocol
{
    public static Task SendAsync(WebSocket socket, object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    public static async Task<string?> ReceiveAsync(
        WebSocket socket,
        int maxMessageBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new GraphQLProtocolException("The GraphQL WebSocket returned a non-text message.", string.Empty);
            if (message.Length + result.Count > maxMessageBytes)
                throw new GraphQLProtocolException(
                    $"The GraphQL WebSocket message exceeded the configured limit of {maxMessageBytes} bytes.",
                    string.Empty);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
        }
    }
}
