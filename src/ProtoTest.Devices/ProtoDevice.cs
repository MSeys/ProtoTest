namespace ProtoTest.Devices;

using ProtoTest.Core;
using ProtoTest.Devices.Internal;

/// <summary>
/// One typed device a suite talks to - a charger, a meter, a sensor. Derive from it, add the domain
/// methods your scenario reads (<c>BootAsync</c>, <c>PlugInAsync</c>), and compose the protected
/// protocol primitives; the instance is created per test by
/// <c>Proto.Context.Devices().For&lt;TDevice&gt;("id")</c> and released with the test.
/// </summary>
/// <remarks>
/// Concurrency: one device instance is one conversation. Connection creation is single-flight and sends
/// are serialized, so concurrent sends share one connection instead of opening one each. One receive may
/// be in flight at a time - a second concurrent receive fails fast with an error naming the device,
/// because it would steal frames from the first. A send that races a disconnect fails with the same
/// named device error instead of a disposed-socket exception. Sends and receives may run concurrently
/// (one in each direction), and the test's end disconnects the device, records
/// <c>device.connected = false</c> and emits the same <c>device.disconnect</c> an explicit call does.
/// </remarks>
public abstract class ProtoDevice
{
    private DeviceSession? _session;

    /// <summary>Gets the device id, as registered and as the trace records it.</summary>
    public string Id => Session.DeviceId;

    private DeviceSession Session => _session ?? throw new InvalidOperationException(
        $"The device {GetType().Name} is not attached to a test. Get it with Proto.Context.Devices().For<{GetType().Name}>(\"id\").");

    /// <summary>Connects the device if it is not connected yet; the transport connects lazily anyway.</summary>
    protected ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        => Session.ConnectAsync(cancellationToken);

    /// <summary>
    /// Starts listening for the device to connect to the test and returns the address to hand the system
    /// under test, with the port the operating system chose. Only a listening transport supports it, such
    /// as a client registered with <c>AddTcpListener</c>; the first send or receive waits for the connection.
    /// </summary>
    protected ValueTask<string> ListenAsync(CancellationToken cancellationToken = default)
        => Session.ListenAsync(cancellationToken);

    /// <summary>Closes the connection; the device can connect again afterwards.</summary>
    protected ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        => Session.DisconnectAsync(cancellationToken);

    /// <summary>Sends one frame and records it as a <c>device.send</c> operation.</summary>
    protected ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
        => Session.SendAsync(frame, cancellationToken);

    /// <summary>Sends one text frame.</summary>
    protected ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
        => Session.SendAsync(DeviceFrame.Text(text), cancellationToken);

    /// <summary>Receives the next frame and records it as a <c>device.receive</c> operation.</summary>
    protected ValueTask<DeviceFrame> ReceiveAsync(CancellationToken cancellationToken = default)
        => Session.ReceiveAsync(cancellationToken);

    /// <summary>
    /// Waits for a frame matching <paramref name="match"/> and records the wait as an asserted
    /// <c>device.command</c>: a match contributes device coverage, a timeout fails with the description
    /// and the frames seen so far.
    /// </summary>
    protected ValueTask<DeviceFrame> ExpectAsync(
        string description,
        Func<DeviceFrame, bool> match,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => Session.ExpectAsync(description, match, timeout, cancellationToken);

    /// <summary>Sends a message type as its data string, as <see cref="DeviceMessage.Format{TMessage}"/> writes it.</summary>
    protected ValueTask SendMessageAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull
        => Session.SendAsync(DeviceMessage.ToFrame(message), cancellationToken);

    /// <summary>
    /// Waits for a text frame that parses as <typeparamref name="TMessage"/> and, when given, satisfies
    /// <paramref name="match"/>; frames that are not that message are skipped. The wait is recorded like
    /// <see cref="ExpectAsync"/>, named after the message type unless <paramref name="description"/> says otherwise.
    /// </summary>
    protected async ValueTask<TMessage> ExpectMessageAsync<TMessage>(
        Func<TMessage, bool>? match = null,
        string? description = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var frame = await Session.ExpectAsync(
            description ?? typeof(TMessage).Name,
            candidate => candidate.TryGetText(out var text)
                && DeviceMessage.TryParse<TMessage>(text, out var message)
                && (match is null || match(message)),
            timeout,
            cancellationToken).ConfigureAwait(false);
        return DeviceMessage.Parse<TMessage>(frame.AsText());
    }

    /// <summary>A readable log of the frames exchanged so far, for failure messages.</summary>
    protected IReadOnlyList<string> Exchange => Session.Exchange;

    /// <summary>Attaches the session when the test creates the device; called by the framework.</summary>
    internal void Attach(DeviceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_session is not null)
        {
            throw new InvalidOperationException($"The device {GetType().Name} is already attached to a test.");
        }

        _session = session;
    }
}
