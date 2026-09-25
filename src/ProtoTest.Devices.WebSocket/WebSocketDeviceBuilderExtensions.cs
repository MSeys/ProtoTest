namespace ProtoTest.Devices.WebSocket;

using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>Registers a WebSocket device client with <c>AddDevices</c>.</summary>
public static class WebSocketDeviceBuilderExtensions
{
    /// <summary>
    /// Declares a named device client over WebSocket. Pass <paramref name="path"/> (or an explicit
    /// <paramref name="address"/>, or a <paramref name="resolveAddress"/> for anything more dynamic);
    /// under an application the address resolves from the application's address. When the application
    /// runs in-process and the in-process transport is registered
    /// (<c>AddInProcessWebSocketDevices&lt;TProgram&gt;()</c>), the path is used through its
    /// <c>TestServer</c> instead, so one registration covers every mode. <c>http(s)</c> addresses
    /// become <c>ws(s)</c>.
    /// </summary>
    public static ProtoDeviceClientBuilder AddWebSocketClient(
        this ProtoDeviceBuilder devices,
        string name,
        string? path = null,
        string? address = null,
        Func<ProtoExecutionContext, string, string>? resolveAddress = null,
        Action<WebSocketDeviceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ProtoOptionsRegistration.Configure<WebSocketDeviceOptions>(devices.Services, () => new WebSocketDeviceOptions(), configure);
        devices.AddTransport<WebSocketDeviceTransport>(WebSocketDeviceTransport.TransportName);

        Func<ProtoExecutionContext, string, string>? resolver = null;
        if (resolveAddress is not null)
        {
            resolver = (context, deviceId) => ToWebSocket(resolveAddress(context, deviceId));
        }
        else if (!string.IsNullOrWhiteSpace(address))
        {
            resolver = ProtoDeviceAddress.Template(address, path, ToWebSocket);
        }
        else if (devices.ApplicationName is { } application)
        {
            resolver = ProtoDeviceAddress.FromApplication(application, path, ToWebSocket);
        }
        else if (path is null)
        {
            throw new InvalidOperationException(
                $"Device client '{name}' has no address. Pass one to AddWebSocketClient, or register it inside AddApplication.");
        }

        return devices.AddClient(name, WebSocketDeviceTransport.TransportName, path, resolver);
    }

    private static string ToWebSocket(string address)
        => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? $"wss://{address[8..]}"
            : address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? $"ws://{address[7..]}"
            : address;
}
