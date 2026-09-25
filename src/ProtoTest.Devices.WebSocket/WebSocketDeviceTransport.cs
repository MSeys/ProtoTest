namespace ProtoTest.Devices.WebSocket;

using ProtoTest.Devices;

/// <summary>
/// Talks to devices over WebSocket - the simulator or gateway in CI, the device's own endpoint in a
/// lab. Text frames stay text, binary frames stay binary; the address comes from the client's resolver.
/// </summary>
public sealed class WebSocketDeviceTransport : IProtoDeviceTransport
{
    /// <summary>The transport's name, as registrations and configuration refer to it.</summary>
    public const string TransportName = "WebSocket";

    private readonly WebSocketDeviceOptions _options;

    /// <summary>Creates the transport with the run's WebSocket options.</summary>
    public WebSocketDeviceTransport(WebSocketDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string Name => TransportName;

    /// <inheritdoc />
    public async ValueTask<IProtoDeviceConnection> ConnectAsync(
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            throw new InvalidOperationException(
                $"'{endpoint.Address}' is not a WebSocket address; use ws:// or wss://.");
        }

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_options.ConnectTimeout);
        var socket = new System.Net.WebSockets.ClientWebSocket();
        if (_options.KeepAliveInterval is { } keepAlive)
        {
            socket.Options.KeepAliveInterval = keepAlive;
        }

        try
        {
            await socket.ConnectAsync(uri, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException(
                $"Connecting to '{uri}' timed out after {_options.ConnectTimeout.TotalSeconds:0.#}s.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new WebSocketDeviceConnection(socket, uri.ToString(), _options);
    }
}

/// <summary>One open WebSocket, frames in and out. Public so a hosting integration can wrap its own socket.</summary>
public sealed class WebSocketDeviceConnection : IProtoDeviceConnection
{
    private const string TextMediaType = "text/plain";
    private const string BinaryMediaType = "application/octet-stream";

    private readonly System.Net.WebSockets.WebSocket _socket;
    private readonly WebSocketDeviceOptions _options;

    public WebSocketDeviceConnection(
        System.Net.WebSockets.WebSocket socket,
        string? remoteAddress,
        WebSocketDeviceOptions options)
    {
        _socket = socket;
        RemoteAddress = remoteAddress;
        _options = options;
    }

    public string? RemoteAddress { get; }

    public ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
    {
        var type = frame.MediaType.StartsWith("text", StringComparison.OrdinalIgnoreCase)
            ? System.Net.WebSockets.WebSocketMessageType.Text
            : System.Net.WebSockets.WebSocketMessageType.Binary;
        return _socket.SendAsync(frame.Payload, type, endOfMessage: true, cancellationToken);
    }

    public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[_options.ReceiveBufferBytes];
        using var message = new MemoryStream();
        var type = System.Net.WebSockets.WebSocketMessageType.Text;
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                return null;
            }

            type = result.MessageType;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        var mediaType = type == System.Net.WebSockets.WebSocketMessageType.Text ? TextMediaType : BinaryMediaType;
        return new DeviceFrame(message.ToArray(), mediaType);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    statusDescription: null,
                    close.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Closing is best-effort; the socket is disposed either way.
        }
        finally
        {
            _socket.Dispose();
        }
    }
}
