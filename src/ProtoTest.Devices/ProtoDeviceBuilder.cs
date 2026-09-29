namespace ProtoTest.Devices;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Devices.Internal;

/// <summary>
/// Configures the device clients a suite talks to, mirroring the REST and GraphQL builders: a named
/// client is declared once with how its addresses resolve, and the typed devices that use it hang off
/// the client.
/// </summary>
public sealed class ProtoDeviceBuilder
{
    private readonly DeviceRegistrationStore _store;

    internal ProtoDeviceBuilder(IServiceCollection services, DeviceRegistrationStore store, string? applicationName)
    {
        Services = services;
        _store = store;
        ApplicationName = applicationName;
    }

    /// <summary>Gets the service collection the clients are registered into; backend packages use it.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the application the builder was registered under, or <see langword="null"/>.</summary>
    public string? ApplicationName { get; }

    /// <summary>
    /// Registers a transport backend by type. Its name defaults to the transport's own
    /// <see cref="IProtoDeviceTransport.Name"/>; a backend package registers its transport for the suite.
    /// </summary>
    public ProtoDeviceBuilder AddTransport<TTransport>(string? name = null)
        where TTransport : class, IProtoDeviceTransport
    {
        if (!_store.Transports.Any(entry => entry.Type == typeof(TTransport)))
        {
            _store.Transports.Add((typeof(TTransport), name));
        }

        return this;
    }

    /// <summary>Registers a transport instance, for a backend the suite configured itself (or a test).</summary>
    public ProtoDeviceBuilder AddTransport(IProtoDeviceTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!_store.TransportInstances.Contains(transport))
        {
            _store.TransportInstances.Add(transport);
        }

        return this;
    }

    /// <summary>Declares a named client whose devices connect through <paramref name="transport"/>.</summary>
    public ProtoDeviceClientBuilder AddClient(
        string name,
        IProtoDeviceTransport transport,
        string? path = null,
        Func<ProtoExecutionContext, string, string>? resolveAddress = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        AddTransport(transport);
        return AddClient(name, transport.Name, path, resolveAddress);
    }

    /// <summary>
    /// Declares a named client by the transport's registered name. <paramref name="resolveAddress"/>
    /// receives the running test and the device id and returns the address to connect to; build one
    /// with <see cref="ProtoDeviceAddress"/> for the common template and application cases.
    /// <paramref name="path"/> is used instead when an in-process transport applies, so one
    /// registration covers both modes.
    /// </summary>
    public ProtoDeviceClientBuilder AddClient(
        string name,
        string transportName,
        string? path = null,
        Func<ProtoExecutionContext, string, string>? resolveAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportName);
        if (path is null && resolveAddress is null)
        {
            throw new ArgumentException(
                $"Device client '{name}' needs an address resolver or a path.", nameof(resolveAddress));
        }

        if (_store.Clients.TryGetValue(name, out var existing))
        {
            throw new InvalidOperationException(
                string.Equals(existing.ApplicationName, ApplicationName, StringComparison.Ordinal)
                    ? $"A device client named '{name}' is already registered."
                    : $"Device client '{name}' is already registered for {DescribeScope(existing.ApplicationName)}; " +
                      $"{DescribeScope(ApplicationName)} cannot reuse the name. Give each application its own client name.");
        }

        var registration = new DeviceClientRegistration(name)
        {
            ApplicationName = ApplicationName,
            ResolveAddress = resolveAddress,
            Path = path,
            TransportName = transportName
        };
        _store.Clients.Add(name, registration);
        _store.DefaultClientName ??= name;
        return new ProtoDeviceClientBuilder(registration);
    }

    /// <summary>Names one side of a client-name collision in the error, host level or an application.</summary>
    private static string DescribeScope(string? applicationName) =>
        applicationName is null ? "the host" : $"application '{applicationName}'";
}

/// <summary>The typed devices and protocol catalogs of one device client.</summary>
public sealed class ProtoDeviceClientBuilder
{
    private readonly DeviceClientRegistration _registration;

    internal ProtoDeviceClientBuilder(DeviceClientRegistration registration) => _registration = registration;

    /// <summary>The client's name, as the context accessor addresses it.</summary>
    public string Name => _registration.Name;

    /// <summary>
    /// Adds a transport setting every device of this client carries on its endpoint. The transport
    /// names its own keys and reads them through <see cref="DeviceEndpoint.Setting"/>; <c>{deviceId}</c>
    /// in a value is filled from the id passed to <c>For</c>, escaped like an address template, and a
    /// repeated key replaces the earlier value. A credential belongs here rather than in the address,
    /// which the trace records.
    /// </summary>
    public ProtoDeviceClientBuilder WithSetting(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _registration.Settings[key] = value;
        return this;
    }

    /// <summary>
    /// Declares the configuration keys the client's address can come from, so the client's device and
    /// transport capabilities are declared only while a key can actually provide an address - a
    /// configured value or a key a registered infrastructure piece declares, exactly like
    /// <c>AddCapabilityWhenProvided</c>. A client whose address is provided in code (an explicit
    /// address or a resolver) declares no keys and keeps its capabilities unconditional. A backend
    /// whose address is configuration-driven calls this; the MQTT transport names
    /// <c>ProtoTest:Devices:Mqtt:Broker</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="keys"/> holds no non-blank key.</exception>
    public ProtoDeviceClientBuilder WithAddressKeys(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var declared = keys.Where(key => !string.IsNullOrWhiteSpace(key)).ToArray();
        if (declared.Length == 0)
        {
            throw new ArgumentException(
                "WithAddressKeys requires at least one configuration key; with no key the client's address cannot be provided.",
                nameof(keys));
        }

        foreach (var key in declared)
        {
            if (!_registration.AddressKeys.Contains(key, StringComparer.Ordinal))
            {
                _registration.AddressKeys.Add(key);
            }
        }

        return this;
    }

    /// <summary>Registers a typed device the client can create; no address or id is configured here.</summary>
    public ProtoDeviceClientBuilder AddDevice<TDevice>()
        where TDevice : ProtoDevice
    {
        if (!_registration.DeviceTypes.Contains(typeof(TDevice)))
        {
            _registration.DeviceTypes.Add(typeof(TDevice));
        }

        return this;
    }

    /// <summary>
    /// Registers a protocol catalog so a matched expectation contributes its message kind and the
    /// coverage report can name the kinds no test asserted.
    /// </summary>
    public ProtoDeviceClientBuilder AddProtocol<TProtocol>()
        where TProtocol : class, IProtoDeviceProtocol
    {
        if (!_registration.ProtocolTypes.Contains(typeof(TProtocol)))
        {
            _registration.ProtocolTypes.Add(typeof(TProtocol));
        }

        return this;
    }
}
