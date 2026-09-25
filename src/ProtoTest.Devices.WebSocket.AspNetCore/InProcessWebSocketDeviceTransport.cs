namespace ProtoTest.Devices.WebSocket.AspNetCore;

using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>
/// Connects to an in-process application's WebSocket endpoint through its <c>TestServer</c>, with no
/// listening socket. The address's path and query are used; host and scheme are ignored. The deviating
/// twin of <see cref="WebSocketDeviceTransport"/>, for endpoints that exist only inside the test process.
/// </summary>
public sealed class InProcessWebSocketDeviceTransport<TProgram>(string? applicationName = null) : IProtoInProcessDeviceTransport
    where TProgram : class
{
    /// <summary>The transport's name, as registrations and configuration refer to it.</summary>
    public const string TransportName = "InProcessWebSocket";

    /// <inheritdoc />
    public string Name => TransportName;

    /// <inheritdoc />
    public bool CanConnect(ProtoExecutionContext context, DeviceEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryServerFactory<TProgram>(applicationName) is not null;
    }

    /// <inheritdoc />
    public async ValueTask<IProtoDeviceConnection> ConnectAsync(
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var context = ProtoHost.CurrentContextOrNull ?? throw new InvalidOperationException(
            $"An in-process device connection needs a running test; '{endpoint.DeviceId}' was reached outside one.");
        var factory = context.ServerFactory<TProgram>(applicationName);
        var path = Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : endpoint.Address;
        // TestServer needs an absolute URI and ignores the authority; the path is what routes.
        var socket = await factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri($"ws://localhost{path}"), cancellationToken)
            .ConfigureAwait(false);
        return new WebSocketDeviceConnection(socket, $"in-process:{path}", new WebSocketDeviceOptions());
    }
}
