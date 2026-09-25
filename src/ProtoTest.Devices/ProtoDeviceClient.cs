namespace ProtoTest.Devices;

using ProtoTest.Core;
using ProtoTest.Devices.Internal;

/// <summary>
/// The devices of one named client, for the current test. <see cref="For{TDevice}"/> creates one typed
/// instance per (client, type, id) and test, resolves its address, attaches it to the client's transport
/// and releases it with the test.
/// </summary>
public sealed class ProtoDeviceClient
{
    private readonly ProtoExecutionContext _context;
    private readonly DeviceClientRegistration _registration;

    internal ProtoDeviceClient(ProtoExecutionContext context, DeviceClientRegistration registration)
    {
        _context = context;
        _registration = registration;
    }

    /// <summary>Gets the client's name, as it was registered and as the trace records it.</summary>
    public string Name => _registration.Name;

    /// <summary>
    /// Gets the device with the given id, creating and attaching it on first use. The class needs a
    /// public parameterless constructor and must be registered on the client.
    /// </summary>
    public TDevice For<TDevice>(string deviceId)
        where TDevice : ProtoDevice
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var key = $"{Name}:{deviceId}";
        if (_context.TryClient<TDevice>(key) is { } existing)
        {
            return existing;
        }

        if (!_registration.DeviceTypes.Contains(typeof(TDevice)))
        {
            throw new InvalidOperationException(
                $"Device client '{Name}' does not register {typeof(TDevice).Name}. Add it with " +
                $"devices.AddClient(\"{Name}\", ...).AddDevice<{typeof(TDevice).Name}>().");
        }

        // An in-process transport wins when it applies, so the same registration works whether the
        // application runs in the test process or behind an address.
        var transports = _context.Service<IEnumerable<IProtoDeviceTransport>>().ToArray();
        var endpoint = new DeviceEndpoint(deviceId, string.Empty);
        var inProcess = _registration.Path is null
            ? null
            : transports
                .OfType<IProtoInProcessDeviceTransport>()
                .FirstOrDefault(candidate => candidate.CanConnect(_context, endpoint));
        IProtoDeviceTransport transport;
        string address;
        if (inProcess is not null)
        {
            transport = inProcess;
            address = _registration.Path!.Replace("{deviceId}", Uri.EscapeDataString(deviceId), StringComparison.Ordinal);
        }
        else
        {
            transport = ResolveTransport(transports);
            address = ResolveAddress(deviceId);
        }

        endpoint = endpoint with { Address = address };
        var protocol = ResolveProtocol();
        var session = new DeviceSession(_context, Name, transport, endpoint, protocol);

        var device = Activator.CreateInstance<TDevice>() ?? throw new InvalidOperationException(
            $"{typeof(TDevice).FullName} could not be created; device classes need a public parameterless constructor.");
        device.Attach(session);
        _context.RegisterClient(device, key);
        _context.RegisterResource(ProtoResource.From(
            $"device:{Name}:{deviceId}",
            "device",
            $"{typeof(TDevice).Name} · {deviceId}",
            (release, _) => session.DisposeAsync()));
        return device;
    }

    private string ResolveAddress(string deviceId)
    {
        var resolver = _registration.ResolveAddress ?? throw new InvalidOperationException(
            $"Device client '{Name}' has no address resolver.");
        var address = resolver(_context, deviceId);
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException($"The address resolver of device client '{Name}' returned no address for '{deviceId}'.");
        }

        return address;
    }

    private IProtoDeviceTransport ResolveTransport(IProtoDeviceTransport[] transports)
    {
        var match = transports.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, _registration.TransportName, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException(
            $"Device client '{Name}' uses the '{_registration.TransportName}' transport, which is not registered. Registered: {Describe(transports.Select(t => t.Name))}.");
    }

    private IProtoDeviceProtocol? ResolveProtocol()
    {
        var protocols = _context.Service<IEnumerable<IProtoDeviceProtocol>>().ToArray();
        if (_registration.ProtocolTypes.Count == 0 || protocols.Length == 0)
        {
            return null;
        }

        return protocols.FirstOrDefault(protocol => _registration.ProtocolTypes.Contains(protocol.GetType()));
    }

    private static string Describe(IEnumerable<string> names) => string.Join(", ", names);
}
