namespace ProtoTest.Devices;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Devices.Internal;

/// <summary>Registers the device clients a suite talks to, on the host or under an application.</summary>
public static class ProtoHostBuilderExtensions
{
    private static readonly ConditionalWeakTable<IServiceCollection, DeviceRegistrationStore> Stores = new();

    /// <summary>
    /// Registers device clients. Each client names its transport and where its devices live; the typed
    /// devices that use it hang off <c>AddClient(...).AddDevice&lt;TDevice&gt;()</c>.
    /// </summary>
    public static IProtoHostBuilder AddDevices(
        this IProtoHostBuilder builder,
        Action<ProtoDeviceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services => Register(services, builder, applicationName: null, configure));
        return builder;
    }

    /// <summary>
    /// Registers device clients under an application: a client without an explicit address uses the
    /// application's address with its path template, so the same suite follows the application into
    /// every environment.
    /// </summary>
    public static IProtoApplicationBuilder AddDevices(
        this IProtoApplicationBuilder application,
        Action<ProtoDeviceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        Register(application.Services, application, application.ApplicationName, configure);
        return application;
    }

    private static void Register(
        IServiceCollection services,
        object capabilityTarget,
        string? applicationName,
        Action<ProtoDeviceBuilder>? configure)
    {
        var store = Stores.GetValue(services, static _ => new DeviceRegistrationStore());
        if (ProtoRegistrationGuard.TryRegisterOnce<DeviceRegistrationMarker>(services))
        {
            services.TryAddSingleton(store);
        }

        configure?.Invoke(new ProtoDeviceBuilder(services, store, applicationName));

        var newTransportTypes = store.Transports
            .Where(entry => store.RegisteredTransportTypes.Add(entry.Type))
            .Select(entry => entry.Type)
            .ToArray();
        var newTransportInstances = store.TransportInstances
            .Where(transport => store.RegisteredTransportInstances.Add(transport))
            .ToArray();
        var newProtocolTypes = store.Clients.Values
            .SelectMany(client => client.ProtocolTypes)
            .Distinct()
            .Where(type => store.RegisteredProtocolTypes.Add(type))
            .ToArray();

        if (newTransportTypes.Length > 0 || newTransportInstances.Length > 0 || newProtocolTypes.Length > 0)
        {
            foreach (var transportType in newTransportTypes)
            {
                services.AddSingleton(typeof(IProtoDeviceTransport), transportType);
            }

            foreach (var transport in newTransportInstances)
            {
                services.AddSingleton(typeof(IProtoDeviceTransport), transport);
            }

            foreach (var protocolType in newProtocolTypes)
            {
                services.AddSingleton(typeof(IProtoDeviceProtocol), protocolType);
            }
        }

        foreach (var client in store.Clients.Values)
        {
            foreach (var deviceType in client.DeviceTypes)
            {
                AddCapability(
                    capabilityTarget,
                    new ProtoCapabilityDescriptor(deviceType.Name, ProtoCapabilityKinds.Device, ProtoDeviceDiagnostics.TraceSource),
                    client.AddressKeys);
            }
        }

        foreach (var (transportType, name) in store.Transports)
        {
            var transportName = name ?? transportType.Name;
            AddCapability(
                capabilityTarget,
                new ProtoCapabilityDescriptor(transportName, ProtoCapabilityKinds.Device, ProtoDeviceDiagnostics.TraceSource),
                TransportAddressKeys(store, transportName));
        }

        foreach (var transport in store.TransportInstances)
        {
            AddCapability(
                capabilityTarget,
                new ProtoCapabilityDescriptor(transport.Name, ProtoCapabilityKinds.Device, ProtoDeviceDiagnostics.TraceSource),
                TransportAddressKeys(store, transport.Name));
        }
    }

    /// <summary>
    /// The address keys that gate one transport's capability: the keys its clients declare, unless a
    /// client with a code-provided address or a path can serve the transport without configuration.
    /// </summary>
    private static IReadOnlyList<string> TransportAddressKeys(DeviceRegistrationStore store, string transportName)
    {
        var keys = new List<string>();
        foreach (var client in store.Clients.Values)
        {
            if (!string.Equals(client.TransportName, transportName, StringComparison.Ordinal))
            {
                continue;
            }

            if (client.AddressKeys.Count == 0)
            {
                return [];
            }

            keys.AddRange(client.AddressKeys);
        }

        return keys;
    }

    private static void AddCapability(
        object target,
        ProtoCapabilityDescriptor capability,
        IReadOnlyCollection<string> addressKeys)
    {
        switch (target)
        {
            case IProtoHostBuilder builder when addressKeys.Count > 0:
                builder.AddCapabilityWhenProvided(capability, [.. addressKeys]);
                break;
            case IProtoApplicationBuilder application when addressKeys.Count > 0:
                application.AddCapabilityWhenProvided(capability, [.. addressKeys]);
                break;
            case IProtoHostBuilder builder:
                builder.AddCapability(capability);
                break;
            case IProtoApplicationBuilder application:
                application.AddCapability(capability);
                break;
        }
    }

    private sealed class DeviceRegistrationMarker;
}
