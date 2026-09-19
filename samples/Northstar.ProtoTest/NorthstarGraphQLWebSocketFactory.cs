namespace Northstar.ProtoTest;

using System.Net.WebSockets;
using global::ProtoTest.AspNetCore;
using global::ProtoTest.Core;
using global::ProtoTest.GraphQL;
using global::ProtoTest.SampleApp;

/// <summary>
/// Connects GraphQL subscriptions through the in-process test server's own WebSocket client, so the
/// subscription rides the same host the application runs in. Registered by
/// <c>AddNorthstarTestSupport(UseInProcessGraphQLWebSockets = true)</c>.
/// </summary>
internal sealed class NorthstarGraphQLWebSocketFactory : IGraphQLWebSocketFactory
{
    public async ValueTask<WebSocket> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var client = Proto.Context.ServerFactory<Program>(NorthstarTargets.Api).Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        client.ConfigureRequest = request =>
        {
            foreach (var header in headers)
            {
                request.Headers[header.Key] = header.Value;
            }
        };
        return await client.ConnectAsync(endpoint, cancellationToken);
    }
}
