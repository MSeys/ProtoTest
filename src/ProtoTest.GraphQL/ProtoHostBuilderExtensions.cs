namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddGraphQL(
        this IProtoHostBuilder builder,
        Action<ProtoGraphQLBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Repeated registration never errors: infrastructure registers once (the hook is deduplicated,
        // the websocket factory and options are TryAdd), while each call's configure callback still
        // runs so a second call composes more clients. A call whose configure throws does not poison
        // the builder.
        builder.ConfigureServices(services =>
        {
            RegisterInfrastructure(services);
            configure?.Invoke(new ProtoGraphQLBuilder(services));
        });
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            "GraphQL", ProtoCapabilityKinds.Protocol, "ProtoTest.GraphQL"));
    }

    /// <summary>
    /// Exposes GraphQL under an application. Named clients are registered relative to the application,
    /// take their endpoint from <c>ProtoTest:Applications:{app}</c>, and become the application's
    /// GraphQL clients in registration order (the first is the default).
    /// </summary>
    public static IProtoApplicationBuilder AddGraphQL(
        this IProtoApplicationBuilder application,
        Action<ProtoGraphQLBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        RegisterInfrastructure(application.Services);
        configure?.Invoke(new ProtoGraphQLBuilder(application.Services, application));
        return application.AddCapability(new ProtoCapabilityDescriptor(
            "GraphQL", ProtoCapabilityKinds.Protocol, "ProtoTest.GraphQL"));
    }

    private static void RegisterInfrastructure(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, GraphQLLifecycleHook>());
        services.TryAddSingleton<IGraphQLWebSocketFactory, ClientGraphQLWebSocketFactory>();
        ProtoHttpOptionsRegistration.TryAddResponseOptions(
            services,
            ProtoGraphQLBuilder.ProtocolName,
            ProtoGraphQLBuilder.ResponsesConfigurationSectionName);
    }
}
