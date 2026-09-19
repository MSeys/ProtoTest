namespace ProtoTest.Rest;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest.Internal;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddRest(this IProtoHostBuilder builder, Action<ProtoRestBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Repeated registration never errors: infrastructure registers once (the hook is deduplicated,
        // the options are keyed TryAdd), while each call's configure callback still runs so a second
        // call composes more clients. A call whose configure throws does not poison the builder.
        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, RestLifecycleHook>());
            ProtoHttpOptionsRegistration.TryAddResponseOptions(
                services,
                ProtoRestBuilder.ProtocolName,
                ProtoRestBuilder.ResponsesConfigurationSectionName);
        });

        if (configure != null)
        {
            builder.ConfigureServices(services =>
            {
                var restBuilder = new ProtoRestBuilder(services);
                configure(restBuilder);
            });
        }

        return builder.AddCapability(new ProtoCapabilityDescriptor(
            "REST", ProtoCapabilityKinds.Protocol, "ProtoTest.Rest"));
    }

    /// <summary>
    /// Exposes REST under an application. Named clients are registered relative to the application,
    /// take their base address from <c>ProtoTest:Applications:{app}</c>, and become the application's
    /// REST clients in registration order (the first is the default).
    /// </summary>
    public static IProtoApplicationBuilder AddRest(
        this IProtoApplicationBuilder application,
        Action<ProtoRestBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, RestLifecycleHook>());
        ProtoHttpOptionsRegistration.TryAddResponseOptions(
            application.Services,
            ProtoRestBuilder.ProtocolName,
            ProtoRestBuilder.ResponsesConfigurationSectionName);
        configure?.Invoke(new ProtoRestBuilder(application.Services, application));
        return application.AddCapability(new ProtoCapabilityDescriptor(
            "REST", ProtoCapabilityKinds.Protocol, "ProtoTest.Rest"));
    }
}
