namespace ProtoTest.Devices.WebSocket.AspNetCore;

using System.Globalization;
using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;
using ProtoTest.AspNetCore;
using ProtoTest.AspNetCore.Internal;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.Devices.WebSocket;

/// <summary>
/// Connects to an in-process application's WebSocket endpoint through its <c>TestServer</c>, with no
/// listening socket. The address's path and query are used; host and scheme are ignored. The deviating
/// twin of <see cref="WebSocketDeviceTransport"/>, for endpoints that exist only inside the test process.
/// </summary>
public sealed class InProcessWebSocketDeviceTransport<TProgram>(
    string? applicationName = null,
    WebSocketDeviceOptions? options = null) : IProtoInProcessDeviceTransport
    where TProgram : class
{
    private readonly string? _applicationName = applicationName;
    private readonly WebSocketDeviceOptions _options = options ?? new WebSocketDeviceOptions();

    /// <summary>The transport's name, as registrations and configuration refer to it.</summary>
    public const string TransportName = "InProcessWebSocket";

    /// <inheritdoc />
    public string Name => TransportName;

    /// <summary>Gets the application this transport serves, or <see langword="null"/> for the test's selected application.</summary>
    public string? ApplicationName => _applicationName;

    /// <summary>Gets the WebSocket options this transport connects and reads with.</summary>
    public WebSocketDeviceOptions Options => _options;

    /// <inheritdoc />
    public bool CanConnect(ProtoExecutionContext context, string? applicationName)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Identity decides, never the endpoint: a client for another application must not be routed
        // through this application's TestServer, even when both expose the same path.
        return string.Equals(applicationName, _applicationName, StringComparison.OrdinalIgnoreCase)
            && context.TryServerFactory<TProgram>(_applicationName) is not null;
    }

    /// <inheritdoc />
    public ValueTask<IProtoDeviceConnection> ConnectAsync(
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
        => ConnectAsync(
            ProtoHost.CurrentContextOrNull ?? throw new InvalidOperationException(
                $"An in-process device connection needs a running test; '{endpoint.DeviceId}' was reached outside one."),
            endpoint,
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IProtoDeviceConnection> ConnectAsync(
        ProtoExecutionContext context,
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(endpoint);
        // The context that decided routing opens the connection: re-reading ambient host state here
        // would throw on a flow without it after CanConnect already said yes.
        var factory = context.ServerFactory<TProgram>(_applicationName);
        var path = Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : endpoint.Address;
        // TestServer needs an absolute URI and ignores the authority; the path is what routes.
        var uri = new Uri($"ws://localhost{path}");
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_options.ConnectTimeout);
        WebSocket socket;
        try
        {
            var client = factory.Server.CreateWebSocketClient();
            // The handshake is an HTTP request the application handles without this flow's test
            // context, so it carries the same identity the in-process HTTP client sends; the
            // application's clock filter then pushes this test's clock while it serves the socket.
            client.ConfigureRequest = ProtoTraceContextHandler.ApplyTo;
            socket = await client
                .ConnectAsync(uri, attempt.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (attempt.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // TestServer can surface an aborted handshake as an incomplete-handshake response instead
            // of an OperationCanceledException; the attempt token is the timeout authority either way.
            throw new TimeoutException(
                $"Connecting to '{path}' in-process timed out after {_options.ConnectTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s.",
                exception);
        }

        return new WebSocketDeviceConnection(socket, $"in-process:{path}", _options);
    }
}
