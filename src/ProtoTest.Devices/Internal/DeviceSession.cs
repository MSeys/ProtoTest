namespace ProtoTest.Devices.Internal;

using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;

/// <summary>
/// One device's life inside one test: the endpoint it talks to, the transport connection created
/// lazily, the frame log for failure messages, and the trace entries and coverage observations its
/// operations produce. The session is registered as a test resource, so the test's end disconnects it.
/// Connection creation is single-flight and sends are serialized; one receive may be in flight at a
/// time, so the device is one conversation, not a frame multiplexer.
/// </summary>
internal sealed class DeviceSession : IAsyncDisposable
{
    private const int MaxLoggedFrames = 50;
    private static readonly TimeSpan DefaultExpectTimeout = TimeSpan.FromSeconds(5);
    /// <summary>How long a disconnect (or the test's release) waits for an in-flight connect.</summary>
    private static readonly TimeSpan ConnectReleaseBound = TimeSpan.FromSeconds(5);

    private readonly ProtoExecutionContext _context;
    private readonly string _clientName;
    private readonly Type _deviceType;
    private readonly IProtoDeviceTransport _transport;
    private readonly DeviceEndpoint _endpoint;
    private readonly IProtoDeviceProtocol? _protocol;
    private readonly List<string> _exchange = [];
    private readonly ProtoLock _gate = new();
    private readonly ProtoLock _connectionGate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _receiveGate = new(1, 1);
    private Task<IProtoDeviceConnection>? _connectionTask;

    public DeviceSession(
        ProtoExecutionContext context,
        string clientName,
        Type deviceType,
        IProtoDeviceTransport transport,
        DeviceEndpoint endpoint,
        IProtoDeviceProtocol? protocol)
    {
        _context = context;
        _clientName = clientName;
        _deviceType = deviceType;
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
        await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // A cancelled disconnect stops before it starts; once it starts it waits for a connect that is
        // already in flight, so the connection is never orphaned between the two calls.
        cancellationToken.ThrowIfCancellationRequested();
        await DisconnectCoreAsync().ConfigureAwait(false);
    }

    public async ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
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
            }
            catch (Exception exception) when (IsDisconnected(connection))
            {
                // A disconnect won the race: report a device error naming the device instead of the
                // transport's disposed-socket exception (audit DEV-3).
                throw new InvalidOperationException(
                    $"The device '{_endpoint.DeviceId}' was disconnected while sending '{frame}'; connect again before sending.",
                    exception);
            }

            Log("→", frame);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask<DeviceFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!_receiveGate.Wait(0))
        {
            // A second reader would steal frames from the first; the device conversation is
            // single-reader, so overlapping receives fail fast instead of racing (audit DEV-3).
            throw new InvalidOperationException(
                $"The device '{_endpoint.DeviceId}' already has a receive in flight; a device reads one frame at a time.");
        }

        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
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
        finally
        {
            _receiveGate.Release();
        }
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
        // The release path is a disconnect: the entity must not stay "connected" after the test, and
        // the trace must show the disconnect like an explicit one (audit DEV-5).
        await DisconnectCoreAsync().ConfigureAwait(false);
    }

    private async ValueTask<IProtoDeviceConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        Task<IProtoDeviceConnection> task;
        lock (_connectionGate)
        {
            task = _connectionTask ??= ConnectCoreAsync();
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (task.IsFaulted)
        {
            // A failed connect does not poison the session: the next use starts a fresh attempt.
            lock (_connectionGate)
            {
                if (ReferenceEquals(_connectionTask, task))
                {
                    _connectionTask = null;
                }
            }

            throw;
        }
    }

    private async Task<IProtoDeviceConnection> ConnectCoreAsync()
    {
        IProtoDeviceConnection connection;
        try
        {
            // The shared connect is not tied to one caller's token: a caller that stops waiting must
            // not cancel the connection another send is about to use. The session's context rides the
            // connect so an in-process transport does not re-read ambient state on another flow.
            connection = await _transport.ConnectAsync(_context, _endpoint, CancellationToken.None).ConfigureAwait(false);
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
        return connection;
    }

    private async ValueTask DisconnectCoreAsync()
    {
        Task<IProtoDeviceConnection>? pending;
        lock (_connectionGate)
        {
            pending = _connectionTask;
            _connectionTask = null;
        }

        if (pending is null)
        {
            return;
        }

        IProtoDeviceConnection connection;
        try
        {
            connection = await pending.WaitAsync(ConnectReleaseBound).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Release must terminate: a transport that never completes its connect is recorded and
            // left behind instead of hanging the test's teardown (audit A5-62).
            _context.Trace.WriteEvent(
                "device.disconnect.abandoned",
                $"Device · {_endpoint.DeviceId} · disconnect abandoned after {ConnectReleaseBound.TotalSeconds:0.#}s",
                ProtoDeviceDiagnostics.TraceSource,
                outcome: ProtoTraceOutcome.Failed,
                attributes: State(connected: false),
                entityKind: ProtoTraceEntityKinds.Device,
                entityId: EntityId);
            return;
        }
        catch (Exception)
        {
            // The connect failed and already recorded why; there is nothing to disconnect.
            return;
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        _context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Device,
            EntityId,
            _endpoint.DeviceId,
            State(connected: false),
            change: "disconnected");
        _context.Trace.WriteEvent(
            "device.disconnect",
            $"Device · {_endpoint.DeviceId} disconnected",
            ProtoDeviceDiagnostics.TraceSource,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: State(connected: false),
            entityKind: ProtoTraceEntityKinds.Device,
            entityId: EntityId);
    }

    private bool IsDisconnected(IProtoDeviceConnection connection)
    {
        lock (_connectionGate)
        {
            return _connectionTask is not { IsCompletedSuccessfully: true } current
                || !ReferenceEquals(current.Result, connection);
        }
    }

    private string EntityId => $"device:{_clientName}:{_deviceType.Name}:{_endpoint.DeviceId}";

    private Dictionary<string, string?> State(bool connected) => new(StringComparer.Ordinal)
    {
        ["device.id"] = _endpoint.DeviceId,
        ["device.client"] = _clientName,
        ["device.type"] = _deviceType.Name,
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
