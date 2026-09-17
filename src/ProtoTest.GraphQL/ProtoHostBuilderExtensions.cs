namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddGraphQL(
        this IProtoHostBuilder builder,
        Action<ProtoGraphQLBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, GraphQLLifecycleHook>());
            services.TryAddSingleton<IGraphQLWebSocketFactory, ClientGraphQLWebSocketFactory>();
            services.TryAddSingleton(sp =>
            {
                var options = new GraphQLResponseOptions();
                options.BindFromConfiguration(sp.GetRequiredService<IConfiguration>());
                return options;
            });
        });
        if (configure is not null)
        {
            builder.ConfigureServices(services => configure(new ProtoGraphQLBuilder(services)));
        }
        return builder;
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
        application.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, GraphQLLifecycleHook>());
        application.Services.TryAddSingleton<IGraphQLWebSocketFactory, ClientGraphQLWebSocketFactory>();
        application.Services.TryAddSingleton(sp =>
        {
            var options = new GraphQLResponseOptions();
            options.BindFromConfiguration(sp.GetRequiredService<IConfiguration>());
            return options;
        });
        configure?.Invoke(new ProtoGraphQLBuilder(application.Services, application));
        return application;
    }
}
