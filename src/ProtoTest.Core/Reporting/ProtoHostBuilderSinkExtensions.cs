namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ProtoHostBuilderSinkExtensions
{
    /// <summary>
    /// Adds a report sink and ensures all registered sinks export at the end of the test run.
    /// The sink is constructed through dependency injection before the optional callback is applied.
    /// </summary>
    public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, Action<TSink>? configure = null)
        where TSink : class, IProtoSink
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            RegisterExportHook(services);
            services.AddSingleton<IProtoSink>(serviceProvider =>
            {
                var sink = ActivatorUtilities.CreateInstance<TSink>(serviceProvider);
                configure?.Invoke(sink);
                if (sink is IProtoConfigurableOptions configurable)
                    configurable.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return sink;
            });
        });
        return builder;
    }

    /// <summary>Adds an existing report sink instance.</summary>
    public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, TSink sink)
        where TSink : class, IProtoSink
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sink);
        builder.ConfigureServices(services =>
        {
            RegisterExportHook(services);
            services.AddSingleton<IProtoSink>(sink);
        });
        return builder;
    }

    private static void RegisterExportHook(IServiceCollection services)
        => services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoSinkExportHook>());
}
