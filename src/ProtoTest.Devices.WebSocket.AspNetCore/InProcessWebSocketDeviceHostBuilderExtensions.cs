namespace ProtoTest.Devices.WebSocket.AspNetCore;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Devices;
using ProtoTest.Devices.WebSocket;

/// <summary>
/// Registers the in-process WebSocket transport. One call covers every environment: it connects
/// through the application's <c>TestServer</c> when the application runs in-process, and declines (so
/// the socket transport and the configured address take over) when it does not.
/// </summary>
public static class InProcessWebSocketDeviceHostBuilderExtensions
{
    /// <summary>
    /// Makes <typeparamref name="TProgram"/>'s endpoints reachable in-process for device clients. Call
    /// it unconditionally; whether it applies is decided per test from whether
    /// <c>AddAspNetCoreServer&lt;TProgram&gt;</c> registered the application.
    /// </summary>
    public static IProtoHostBuilder AddInProcessWebSocketDevices<TProgram>(
        this IProtoHostBuilder builder,
        string applicationName,
        Action<WebSocketDeviceOptions>? configure = null)
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        builder.ConfigureServices(services =>
        {
            ProtoOptionsRegistration.Configure<WebSocketDeviceOptions>(services, () => new WebSocketDeviceOptions(), configure);
            if (ProtoRegistrationGuard.TryRegisterOnce<InProcessWebSocketRegistration>(services))
            {
                services.AddSingleton<IProtoDeviceTransport>(
                    new InProcessWebSocketDeviceTransport<TProgram>(applicationName));
            }
        });

        return builder.AddCapability(new ProtoCapabilityDescriptor(
            InProcessWebSocketDeviceTransport<TProgram>.TransportName,
            ProtoCapabilityKinds.Device,
            ProtoDeviceDiagnostics.TraceSource));
    }

    private sealed class InProcessWebSocketRegistration;
}
