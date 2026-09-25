namespace ProtoTest.Devices;

using ProtoTest.Core;

/// <summary>
/// Where a device lives: its id, the address its transport connects to, and any protocol settings the
/// registration carries (device model, credentials, timeouts).
/// </summary>
public sealed record DeviceEndpoint(
    string DeviceId,
    string Address,
    IReadOnlyDictionary<string, string?>? Settings = null)
{
    public string? Setting(string key) => Settings is not null && Settings.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// A protocol backend: the WebSocket implementation lives in its own package, and a test transport can
/// stand in for it. A transport only moves frames; the device classes above it own the conversation.
/// </summary>
public interface IProtoDeviceTransport
{
    /// <summary>Gets the transport's name, as configuration and registrations refer to it.</summary>
    string Name { get; }

    /// <summary>Connects to a device endpoint; the connection is opened lazily on first use.</summary>
    ValueTask<IProtoDeviceConnection> ConnectAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>One open connection to a device; frames in, frames out.</summary>
public interface IProtoDeviceConnection : IAsyncDisposable
{
    /// <summary>Gets the address the connection reached, when the transport can report it.</summary>
    string? RemoteAddress { get; }

    /// <summary>Sends one frame.</summary>
    ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default);

    /// <summary>Receives the next frame, or <see langword="null"/> when the device closed the connection.</summary>
    ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A transport for endpoints hosted inside the test process. A client prefers it over its named
/// transport whenever it applies, so one client registration works in-process and against an address
/// without the suite branching.
/// </summary>
public interface IProtoInProcessDeviceTransport : IProtoDeviceTransport
{
    /// <summary>Whether this transport can reach <paramref name="endpoint"/> right now.</summary>
    bool CanConnect(ProtoExecutionContext context, DeviceEndpoint endpoint);
}

/// <summary>
/// What a transport's messages mean, for tracing and coverage: the protocol's message kinds and how a
/// frame is classified to one of them. The WebSocket backend ships the OCPP subset this way; the
/// coverage collector reports every entry covered or not, so an untested message kind shows as a gap.
/// </summary>
public interface IProtoDeviceProtocol
{
    /// <summary>Gets the protocol's name, for example "OCPP 1.6J".</summary>
    string Name { get; }

    /// <summary>Gets every message kind the protocol supports.</summary>
    IReadOnlyList<DeviceProtocolEntry> Entries { get; }

    /// <summary>Classifies a frame to a message kind, or <see langword="null"/> when it is unknown to this protocol.</summary>
    string? Classify(DeviceFrame frame);
}

/// <summary>One message kind a device protocol supports.</summary>
public sealed record DeviceProtocolEntry(string Kind, string? Description = null);
