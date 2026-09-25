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
        var registeredTransport = false;
        builder.ConfigureServices(services =>
        {
            ProtoOptionsRegistration.Configure<WebSocketDeviceOptions>(services, () => new WebSocketDeviceOptions(), configure);
            registeredTransport = ProtoRegistrationGuard.TryRegisterOnce<InProcessWebSocketRegistration>(services);
            if (registeredTransport)
            {
                services.AddSingleton<IProtoDeviceTransport>(
                    new InProcessWebSocketDeviceTransport<TProgram>(applicationName));
            }
        });

        if (!registeredTransport)
        {
            return builder;
        }

        // The transport only serves an application that runs in-process. When the environment publishes
        // the application's address the transport declines and the socket takes over, so the capability
        // steps aside with it instead of advertising a transport the run will not use (audit REG-5).
        return builder.AddCapabilityUnlessConfigured(
            new ProtoCapabilityDescriptor(
                InProcessWebSocketDeviceTransport<TProgram>.TransportName,
                ProtoCapabilityKinds.Device,
                ProtoDeviceDiagnostics.TraceSource)
            {
                Instance = applicationName
            },
            $"ProtoTest:Applications:{applicationName}:BaseUrl");
    }

    private sealed class InProcessWebSocketRegistration;
}
