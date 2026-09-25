namespace ProtoTest.Devices.Internal;

using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;

/// <summary>
/// One device's life inside one test: the endpoint it talks to, the transport connection created
/// lazily, the frame log for failure messages, and the trace entries and coverage observations its
/// operations produce. The session is registered as a test resource, so the test's end disconnects it.
/// </summary>
internal sealed class DeviceSession : IAsyncDisposable
{
    private const int MaxLoggedFrames = 50;
    private static readonly TimeSpan DefaultExpectTimeout = TimeSpan.FromSeconds(5);

    private readonly ProtoExecutionContext _context;
    private readonly string _clientName;
    private readonly IProtoDeviceTransport _transport;
    private readonly DeviceEndpoint _endpoint;
    private readonly IProtoDeviceProtocol? _protocol;
    private readonly List<string> _exchange = [];
    private readonly ProtoLock _gate = new();
    private IProtoDeviceConnection? _connection;

    public DeviceSession(
        ProtoExecutionContext context,
        string clientName,
        IProtoDeviceTransport transport,
        DeviceEndpoint endpoint,
        IProtoDeviceProtocol? protocol)
    {
        _context = context;
        _clientName = clientName;
        _transport = transport;
        _endpoint = endpoint;
        _protocol = protocol;
    }

    public string DeviceId => _endpoint.DeviceId;

    public IReadOnlyList<string> Exchange
    {
        get
        {
            lock (_gate)
            {
                return [.. _exchange];
            }
        }
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return;
        }

        IProtoDeviceConnection connection;
        try
        {
            connection = await _transport.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _context.Trace.SetEntityState(
                ProtoTraceEntityKinds.Device,
                EntityId,
                _endpoint.DeviceId,
                State(connected: false),
                change: "connect-failed");
            throw new InvalidOperationException(
                $"The device '{_endpoint.DeviceId}' could not connect to '{_endpoint.Address}' over {_transport.Name}: {exception.Message}",
                exception);
        }

        _connection = connection;
        _context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Device,
            EntityId,
            _endpoint.DeviceId,
            State(connected: true),
            change: "connected");
        _context.Trace.WriteEvent(
            "device.connect",
            $"Device · {_endpoint.DeviceId} · {_transport.Name}",
            ProtoDeviceDiagnostics.TraceSource,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: State(connected: true),
            entityKind: ProtoTraceEntityKinds.Device,
            entityId: EntityId);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connection;
        _connection = null;
        if (connection is null)
        {
            return;
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        _context.Trace.WriteEvent(
            "device.disconnect",
            $"Device · {_endpoint.DeviceId} disconnected",
            ProtoDeviceDiagnostics.TraceSource,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: State(connected: false),
            entityKind: ProtoTraceEntityKinds.Device,
            entityId: EntityId);
    }

    public async ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        var connection = _connection!;
        await _context.Trace.ExecuteAsync(
            "device.send",
            $"{_transport.Name} · {frame}",
            ProtoDeviceDiagnostics.TraceSource,
            async operation =>
            {
                operation
                    .SetAttribute("device.id", _endpoint.DeviceId)
                    .SetAttribute("device.transport", _transport.Name)
                    .SetAttribute("device.frame.kind", _protocol?.Classify(frame))
                    .SetAttribute("device.frame", frame.ToString());
                await connection.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            },
            attributes: State(connected: true),
            entityKind: ProtoTraceEntityKinds.Device,
            entityId: EntityId).ConfigureAwait(false);
        Log("→", frame);
    }

    public async ValueTask<DeviceFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        var connection = _connection!;
        var frame = await _context.Trace.ExecuteAsync(
            "device.receive",
            $"{_transport.Name} · receive",
            ProtoDeviceDiagnostics.TraceSource,
            async operation =>
            {
                var received = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"The device '{_endpoint.DeviceId}' closed the connection.");
                operation
                    .SetAttribute("device.id", _endpoint.DeviceId)
                    .SetAttribute("device.frame.kind", _protocol?.Classify(received))
                    .SetAttribute("device.frame", received.ToString());
                return received;
            },
            attributes: State(connected: true),
            entityKind: ProtoTraceEntityKinds.Device,
            entityId: EntityId).ConfigureAwait(false);
        Log("←", frame);
        return frame;
    }

    public async ValueTask<DeviceFrame> ExpectAsync(
        string description,
        Func<DeviceFrame, bool> match,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(match);
        var wait = timeout ?? DefaultExpectTimeout;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(wait);

        return await _context.Trace.ExecuteAsync(
            "device.command",
            description,
            ProtoDeviceDiagnostics.TraceSource,
            async operation =>
            {
                operation
                    .SetAttribute("device.id", _endpoint.DeviceId)
                    .SetAttribute("device.transport", _transport.Name);
                while (true)
                {
                    DeviceFrame frame;
                    try
                    {
                        frame = await ReceiveAsync(attempt.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var message =
                            $"{description} was not observed within {wait.TotalSeconds:0.#}s on device '{_endpoint.DeviceId}'." +
                            Environment.NewLine + "Frames exchanged: " + (Exchange.Count == 0 ? "(none)" : string.Join(Environment.NewLine, Exchange));
                        throw new DeviceAssertionException(message);
                    }

                    if (!match(frame))
                    {
                        continue;
                    }

                    var kind = _protocol?.Classify(frame) ?? description;
                    operation.SetAttribute("device.command.kind", kind);
                    _context.RecordObservation(_clientName, "device.operation", kind, data: frame.ToString());
                    return frame;
                }
            },
            attributes: State(connected: true),
            entityKind: ProtoTraceEntityKinds.Device,
            entityId: EntityId).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string EntityId => $"device:{_clientName}:{_endpoint.DeviceId}";

    private Dictionary<string, string?> State(bool connected) => new(StringComparer.Ordinal)
    {
        ["device.id"] = _endpoint.DeviceId,
        ["device.client"] = _clientName,
        ["device.transport"] = _transport.Name,
        ["device.address"] = _endpoint.Address,
        ["device.connected"] = connected ? "true" : "false"
    };

    private void Log(string direction, DeviceFrame frame)
    {
        lock (_gate)
        {
            _exchange.Add($"{direction} {frame}");
            if (_exchange.Count > MaxLoggedFrames)
            {
                _exchange.RemoveAt(0);
            }
        }
    }
}
