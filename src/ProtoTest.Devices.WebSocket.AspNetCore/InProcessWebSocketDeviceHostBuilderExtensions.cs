namespace ProtoTest.Devices.WebSocket.AspNetCore;

using System.Runtime.CompilerServices;
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
    // One registration per (program, application): a second TProgram, or the same program under
    // another application, is a second transport instead of a silently dropped duplicate.
    private static readonly ConditionalWeakTable<IServiceCollection, HashSet<(Type Program, string Application)>> Registrations = new();

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
            var registrations = Registrations.GetValue(services, static _ => new HashSet<(Type, string)>());
            registeredTransport = registrations.Add((typeof(TProgram), applicationName));
            if (registeredTransport)
            {
                // The registered options resolve (and validate) when the transport resolves, exactly
                // like the socket transport, so configured values are the ones the connection uses.
                services.AddSingleton<IProtoDeviceTransport>(provider =>
                    new InProcessWebSocketDeviceTransport<TProgram>(
                        applicationName,
                        provider.GetRequiredService<WebSocketDeviceOptions>()));
            }
        });

        if (!registeredTransport)
        {
            return builder;
        }

        // The transport only serves an application that runs in-process. When the environment publishes
        // the application's address the transport declines and the socket takes over, so the capability
        // steps aside with it instead of advertising a transport the run will not use.
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
}
