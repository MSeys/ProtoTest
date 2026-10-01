namespace ProtoTest.Devices.Tcp;

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ProtoTest.Devices;

/// <summary>
/// Connects devices out over a raw TCP socket: the device under the test dials the system under test, a
/// gateway or a simulator at <c>tcp://host:port</c>. The client's framer cuts the stream into frames; a
/// client without one reads newline-terminated text.
/// </summary>
public sealed class TcpDeviceTransport : IProtoDeviceTransport
{
    /// <summary>The transport's name, as registrations and the trace refer to it.</summary>
    public const string TransportName = "TCP";

    private readonly TcpDeviceOptions _options;

    /// <summary>Creates the transport with the run's TCP options.</summary>
    public TcpDeviceTransport(TcpDeviceOptions options)
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
        var (host, port) = TcpDeviceAddress.Parse(endpoint.Address);
        var client = new TcpClient { NoDelay = _options.NoDelay };
        var target = TcpDeviceAddress.Format(host, port);
        try
        {
            await ProtoDeviceConnect
                .WithTimeoutAsync(
                    target,
                    _options.ConnectTimeout,
                    async token =>
                    {
                        await client.ConnectAsync(host, port, token).ConfigureAwait(false);
                        return client;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new StreamDeviceConnection(
            client.GetStream(),
            endpoint.Framer ?? DeviceFramers.Lines(),
            target,
            _options.MaxFrameBytes,
            new DisposableOwner(client));
    }
}

/// <summary>
/// Listens for the system under test to connect to the device: the test opens a port, hands the address
/// to the application, and the first send or receive waits for the inbound connection. Each device gets
/// its own listener, so parallel tests never share a port.
/// </summary>
public sealed class TcpListenerDeviceTransport : IProtoDeviceTransport
{
    /// <summary>The transport's name, as registrations and the trace refer to it.</summary>
    public const string TransportName = "TCP listener";

    private readonly TcpDeviceOptions _options;

    /// <summary>Creates the transport with the run's TCP options.</summary>
    public TcpListenerDeviceTransport(TcpDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string Name => TransportName;

    /// <inheritdoc />
    public ValueTask<IProtoDeviceConnection> ConnectAsync(
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var (host, port) = TcpDeviceAddress.Parse(endpoint.Address);
        if (!IPAddress.TryParse(host, out var address))
        {
            address = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                ? IPAddress.Loopback
                : throw new InvalidOperationException(
                    $"A TCP listener binds an IP address, not '{host}'. Use tcp://127.0.0.1:0, or tcp://0.0.0.0:0 for every interface.");
        }

        var listener = new TcpListener(address, port);
        listener.Start(backlog: 1);
        var bound = (IPEndPoint)listener.LocalEndpoint;
        var listenAddress = TcpDeviceAddress.Format(
            address.Equals(IPAddress.Any) ? IPAddress.Loopback.ToString() : address.ToString(),
            bound.Port);
        return ValueTask.FromResult<IProtoDeviceConnection>(new TcpListeningConnection(
            listener,
            listenAddress,
            endpoint.Framer ?? DeviceFramers.Lines(),
            _options));
    }
}

/// <summary>A device connection that accepts one inbound socket, then frames it like an outbound one.</summary>
internal sealed class TcpListeningConnection(
    TcpListener listener,
    string listenAddress,
    IDeviceFramer framer,
    TcpDeviceOptions options) : IProtoDeviceConnection, IProtoDeviceListener
{
    private readonly ProtoLock _gate = new();
    private Task<StreamDeviceConnection>? _accepted;
    private bool _disposed;

    public string ListenAddress { get; } = listenAddress;

    public string? RemoteAddress { get; private set; }

    public async ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
        => await (await AcceptedAsync(cancellationToken).ConfigureAwait(false)).SendAsync(frame, cancellationToken).ConfigureAwait(false);

    public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
        => await (await AcceptedAsync(cancellationToken).ConfigureAwait(false)).ReceiveAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        Task<StreamDeviceConnection>? accepted;
        lock (_gate)
        {
            _disposed = true;
            accepted = _accepted;
        }

        listener.Stop();
        if (accepted is { IsCompletedSuccessfully: true })
        {
            await accepted.Result.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task<StreamDeviceConnection> AcceptedAsync(CancellationToken cancellationToken)
    {
        Task<StreamDeviceConnection> task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            task = _accepted ??= AcceptCoreAsync();
        }

        return task.WaitAsync(cancellationToken);
    }

    private async Task<StreamDeviceConnection> AcceptCoreAsync()
    {
        using var attempt = new CancellationTokenSource(options.AcceptTimeout);
        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync(attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (attempt.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Nothing connected to '{ListenAddress}' within " +
                $"{options.AcceptTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s. Hand the address " +
                "ListenAsync returned to the system under test before the device's first send or receive.",
                exception);
        }

        // One device is one conversation: the listener stops once its connection arrived.
        listener.Stop();
        client.NoDelay = options.NoDelay;
        RemoteAddress = client.Client.RemoteEndPoint?.ToString();
        return new StreamDeviceConnection(
            client.GetStream(),
            framer,
            RemoteAddress ?? ListenAddress,
            options.MaxFrameBytes,
            new DisposableOwner(client));
    }
}

/// <summary>Parses and formats the <c>tcp://host:port</c> addresses both TCP transports use.</summary>
public static class TcpDeviceAddress
{
    /// <summary>Splits <c>tcp://host:port</c> into its host and port, or fails naming the expected form.</summary>
    public static (string Host, int Port) Parse(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!address.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Port < 0
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException($"'{address}' is not a TCP device address; use tcp://host:port.");
        }

        return (uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host, uri.Port);
    }

    /// <summary>Formats a host and port as a <c>tcp://</c> address, bracketing an IPv6 host.</summary>
    public static string Format(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var shown = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return $"tcp://{shown}:{port.ToString(CultureInfo.InvariantCulture)}";
    }
}

/// <summary>Disposes a socket client alongside the stream it handed out.</summary>
internal sealed class DisposableOwner(IDisposable owned) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        owned.Dispose();
        return ValueTask.CompletedTask;
    }
}
