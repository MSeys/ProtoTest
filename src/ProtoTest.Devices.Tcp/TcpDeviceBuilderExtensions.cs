namespace ProtoTest.Devices.Tcp;

using System.Globalization;
using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>Registers device clients that talk TCP, connecting out or listening for the system under test.</summary>
public static class TcpDeviceBuilderExtensions
{
    /// <summary>
    /// Declares a named device client whose devices connect out over TCP. Pass an
    /// <paramref name="address"/> (<c>tcp://host:port</c>, <c>{deviceId}</c> allowed), a
    /// <paramref name="resolveAddress"/>, or, inside <c>AddApplication</c>, a <paramref name="port"/> on the
    /// application's host. <paramref name="framer"/> cuts the stream into frames; without one each frame is
    /// a line of text ending in <c>\n</c>.
    /// </summary>
    public static ProtoDeviceClientBuilder AddTcpClient(
        this ProtoDeviceBuilder devices,
        string name,
        IDeviceFramer? framer = null,
        string? address = null,
        int? port = null,
        Func<ProtoExecutionContext, string, string>? resolveAddress = null,
        Action<TcpDeviceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "A TCP port is between 1 and 65535.");
        }

        ProtoOptionsRegistration.Configure<TcpDeviceOptions>(devices.Services, () => new TcpDeviceOptions(), configure);
        devices.AddTransport<TcpDeviceTransport>(TcpDeviceTransport.TransportName);

        Func<ProtoExecutionContext, string, string> resolver;
        if (resolveAddress is not null)
        {
            resolver = resolveAddress;
        }
        else if (!string.IsNullOrWhiteSpace(address))
        {
            TcpDeviceAddress.Parse(address.Replace("{deviceId}", "id", StringComparison.Ordinal));
            resolver = ProtoDeviceAddress.Template(address);
        }
        else if (devices.ApplicationName is { } application && port is { } applicationPort)
        {
            resolver = ProtoDeviceAddress.FromApplication(application, transform: baseUrl => OnApplicationHost(baseUrl, applicationPort));
        }
        else
        {
            throw new InvalidOperationException(
                $"TCP device client '{name}' has no address. Pass address: \"tcp://host:port\", a resolver, " +
                "or register it inside AddApplication with a port.");
        }

        return Framed(devices.AddClient(name, TcpDeviceTransport.TransportName, resolveAddress: resolver), framer);
    }

    /// <summary>
    /// Declares a named device client whose devices listen for the system under test to connect. Each
    /// device binds its own port on <paramref name="host"/> (port 0 lets the operating system choose), and
    /// <c>ListenAsync</c> in the device class returns the address to hand the application.
    /// </summary>
    public static ProtoDeviceClientBuilder AddTcpListener(
        this ProtoDeviceBuilder devices,
        string name,
        IDeviceFramer? framer = null,
        string host = "127.0.0.1",
        int port = 0,
        Action<TcpDeviceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "A TCP port is between 0 and 65535; 0 lets the system choose.");
        }

        ProtoOptionsRegistration.Configure<TcpDeviceOptions>(devices.Services, () => new TcpDeviceOptions(), configure);
        devices.AddTransport<TcpListenerDeviceTransport>(TcpListenerDeviceTransport.TransportName);
        var address = TcpDeviceAddress.Format(host, port);
        return Framed(
            devices.AddClient(name, TcpListenerDeviceTransport.TransportName, resolveAddress: (_, _) => address),
            framer);
    }

    private static ProtoDeviceClientBuilder Framed(ProtoDeviceClientBuilder client, IDeviceFramer? framer)
        => framer is null ? client : client.WithFramer(framer);

    private static string OnApplicationHost(string baseUrl, int port)
    {
        var host = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.Trim('[', ']')
            : throw new InvalidOperationException($"The application address '{baseUrl}' has no host for a TCP device client.");
        return TcpDeviceAddress.Format(host, port);
    }
}
