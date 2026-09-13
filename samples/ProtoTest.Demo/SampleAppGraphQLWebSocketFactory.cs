namespace ProtoTest.Demo;

using System.Net.WebSockets;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.SampleApp;
using ProtoTest.SampleApp.Testing;

internal sealed class SampleAppGraphQLWebSocketFactory : IGraphQLWebSocketFactory
{
    public async ValueTask<WebSocket> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var client = Proto.Context.Server<Program>(SampleAppTargets.Api).Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        client.ConfigureRequest = request =>
        {
            foreach (var header in headers)
                request.Headers[header.Key] = header.Value;
        };
        return await client.ConnectAsync(endpoint, cancellationToken);
    }
}
