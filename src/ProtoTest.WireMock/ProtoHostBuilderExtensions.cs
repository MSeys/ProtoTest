namespace ProtoTest.WireMock;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.WireMock.Internal;

/// <summary>Registers fake HTTP services the suite stubs per test.</summary>
public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Marks the WireMock test hook, so it is registered once however many fakes the suite composes.
    /// </summary>
    private sealed class WireMockHookRegistration;

    /// <summary>
    /// Adds a fake HTTP service the tests stub: <c>Proto.Context.WireMock(name)</c> starts its server
    /// on first use, stubs read like the scenario, matched requests are traced with the REST response
    /// shape, and registered stubs contribute coverage. A repeated name with equal settings composes;
    /// a repeated name with different settings throws naming the fake.
    /// </summary>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="name">The fake's name, in accessors, capabilities and diagnostics. Defaults to "Default".</param>
    /// <param name="configure">The fake's lifetime and port; per-test on a dynamic port by default.</param>
    public static IProtoHostBuilder AddWireMock(
        this IProtoHostBuilder builder,
        string name = "Default",
        Action<ProtoWireMockBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        builder.ConfigureServices(services =>
        {
            var fake = new ProtoWireMockBuilder(name);
            configure?.Invoke(fake);
            var settings = fake.Build();

            var registry = ProtoWireMockRegistry.GetOrAdd(services);
            var created = registry.AddOrThrow(name, settings);
            if (created)
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoCollector>(
                    new WireMockCoverageCollector(ProtoWireMockProtocol.TargetName(name))));
                if (settings.PerRun)
                {
                    builder.AddResource(new ProtoResource(
                        $"wiremock:{name}",
                        "wiremock",
                        $"WireMock fake '{name}'",
                        context =>
                        {
                            if (registry.TryRemoveRunSession(name, out var session) && session is not null)
                            {
                                session.Stop(context.Trace, scope: null);
                            }

                            return ValueTask.CompletedTask;
                        },
                        ProtoResourceScope.Run));
                }
            }

            if (ProtoRegistrationGuard.TryRegisterOnce<WireMockHookRegistration>(services))
            {
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IProtoTestHook, ProtoWireMockTestHook>());
            }

            builder.AddCapability(new ProtoCapabilityDescriptor(
                ProtoWireMockProtocol.Name,
                ProtoCapabilityKinds.Protocol,
                ProtoWireMockProtocol.TraceSource)
            {
                Instance = name
            });
        });
        return builder;
    }
}
