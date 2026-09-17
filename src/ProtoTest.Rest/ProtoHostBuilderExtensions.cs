namespace ProtoTest.Rest;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Rest.Internal;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddRest(this IProtoHostBuilder builder, Action<ProtoRestBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, RestLifecycleHook>());
            services.TryAddSingleton(serviceProvider =>
            {
                var options = new RestResponseOptions();
                options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return options;
            });
        });

        if (configure != null)
        {
            builder.ConfigureServices(services =>
            {
                var restBuilder = new ProtoRestBuilder(services);
                configure(restBuilder);
            });
        }

        return builder;
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
        application.Services.TryAddSingleton(serviceProvider =>
        {
            var options = new RestResponseOptions();
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
        configure?.Invoke(new ProtoRestBuilder(application.Services, application));
        return application;
    }
}
