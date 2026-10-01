namespace ProtoTest.Devices.Serial;

using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>Registers device clients on a serial line.</summary>
public static class SerialDeviceBuilderExtensions
{
    /// <summary>
    /// Declares a named device client on a serial port. Pass an <paramref name="address"/>
    /// (<c>serial://COM3?baud=9600</c>) or a <paramref name="resolveAddress"/>; without either, the port
    /// comes from <c>ProtoTest:Devices:Serial:Ports:{name}</c>, and the client's devices exist only where
    /// that key is set, so <c>[RequiresDevice]</c> skips a hardware test on a machine without the device.
    /// <paramref name="framer"/> cuts the line into frames; without one each frame is a line of text ending
    /// in <c>\n</c>.
    /// </summary>
    public static ProtoDeviceClientBuilder AddSerialClient(
        this ProtoDeviceBuilder devices,
        string name,
        IDeviceFramer? framer = null,
        string? address = null,
        Func<ProtoExecutionContext, string, string>? resolveAddress = null,
        Action<SerialDeviceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (address is not null)
        {
            SerialPortAddress.Parse(address);
        }

        ProtoOptionsRegistration.Configure<SerialDeviceOptions>(devices.Services, () => new SerialDeviceOptions(), configure);
        devices.AddTransport<SerialDeviceTransport>(SerialDeviceTransport.TransportName);

        var portKey = $"{SerialDeviceOptions.PortsSection}:{name}";
        var resolver = resolveAddress
            ?? (address is not null
                ? ProtoDeviceAddress.Template(address)
                : (context, deviceId) =>
                    ProtoApplication.ResolveSetting(context.Configuration, context.TryService<ProtoInfrastructureSettings>(), portKey)
                    ?? throw new InvalidOperationException(
                        $"Serial device client '{name}' has no port for '{deviceId}'. Pass address: \"serial://COM3?baud=9600\", " +
                        $"a resolver, or set '{portKey}'."));
        var client = devices.AddClient(name, SerialDeviceTransport.TransportName, resolveAddress: resolver);
        if (address is null && resolveAddress is null)
        {
            client.WithAddressKeys(portKey);
        }

        return framer is null ? client : client.WithFramer(framer);
    }
}
