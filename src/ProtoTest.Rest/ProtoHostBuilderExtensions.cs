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
                options.Bind(serviceProvider.GetRequiredService<IConfiguration>());
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
}
