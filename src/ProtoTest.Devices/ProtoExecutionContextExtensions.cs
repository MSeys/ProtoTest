namespace ProtoTest.Devices;

using ProtoTest.Core;
using ProtoTest.Devices.Internal;

/// <summary>Reaches the current test's device clients.</summary>
public static class ProtoExecutionContextExtensions
{
    private const string RegistryName = "ProtoTest.Devices";

    /// <summary>
    /// Gets the devices of a named client; without a name, the only registered client. Mirrors the REST
    /// and GraphQL accessors: the client is chosen here, the device id at <c>For</c>.
    /// </summary>
    public static ProtoDeviceClient Devices(this ProtoExecutionContext context, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var store = context.TryService<DeviceRegistrationStore>() ?? throw new InvalidOperationException(
            "This host was not composed with ProtoTest.Devices; register devices with builder.AddDevices(...).");

        var name = clientName ?? store.DefaultClientName;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "No device client is registered. Register one with builder.AddDevices(devices => devices.AddClient(...)).");
        }

        if (!store.Clients.TryGetValue(name, out var registration))
        {
            throw new InvalidOperationException(
                $"No device client named '{name}' is registered. Registered: {string.Join(", ", store.Clients.Keys)}.");
        }

        if (clientName is null && store.Clients.Count > 1)
        {
            throw new InvalidOperationException(
                $"Several device clients are registered ({string.Join(", ", store.Clients.Keys)}); pass the name to Devices(name).");
        }

        var key = $"{RegistryName}:{registration.Name}";
        if (context.TryClient<ProtoDeviceClient>(key) is { } existing)
        {
            return existing;
        }

        var client = new ProtoDeviceClient(context, registration);
        context.RegisterClient(client, key);
        return client;
    }
}
