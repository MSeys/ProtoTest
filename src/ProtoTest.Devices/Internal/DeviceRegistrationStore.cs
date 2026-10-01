namespace ProtoTest.Devices.Internal;

using ProtoTest.Core;

/// <summary>
/// One named device client: where its devices connect (resolved per test), the transport settings it
/// carries (resolved per device) and which typed devices and protocols hang off it. There is no
/// per-device configuration; the address and the settings are functions of the context and the device id.
/// </summary>
internal sealed class DeviceClientRegistration(string name)
{
    public string Name { get; } = name;

    /// <summary>The application the client was registered under, or null for a host-level client.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Resolves the address for one device id.</summary>
    public Func<ProtoExecutionContext, string, string>? ResolveAddress { get; set; }

    /// <summary>A path template for an in-process endpoint, possibly carrying <c>{deviceId}</c>.</summary>
    public string? Path { get; set; }

    /// <summary>The transport's name, as registered with <c>AddTransport</c>.</summary>
    public string TransportName { get; set; } = string.Empty;

    /// <summary>The client's transport settings, keyed by the transport's own names, filled per device.</summary>
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The configuration keys the client's address can come from, declared by a backend whose address
    /// is configuration-driven; empty when the address is provided in code or by a path, which keeps
    /// the client's capabilities unconditional.
    /// </summary>
    public List<string> AddressKeys { get; } = [];

    /// <summary>How a stream transport frames this client's bytes, or null for the transport's default.</summary>
    public IDeviceFramer? Framer { get; set; }

    public List<Type> DeviceTypes { get; } = [];

    public List<Type> ProtocolTypes { get; } = [];
}

/// <summary>
/// The device clients, transports and protocols a suite registered. Built while composing the host and
/// read per test; keyed by the host's service collection so host-level and application-level
/// registrations compose into one set.
/// </summary>
internal sealed class DeviceRegistrationStore
{
    public List<(Type Type, string? Name)> Transports { get; } = [];

    public List<IProtoDeviceTransport> TransportInstances { get; } = [];

    public Dictionary<string, DeviceClientRegistration> Clients { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? DefaultClientName { get; set; }

    public HashSet<Type> RegisteredTransportTypes { get; } = [];

    public HashSet<IProtoDeviceTransport> RegisteredTransportInstances { get; } = [];

    public HashSet<Type> RegisteredProtocolTypes { get; } = [];
}
