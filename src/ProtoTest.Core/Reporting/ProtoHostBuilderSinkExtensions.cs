namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ProtoHostBuilderSinkExtensions
{
    /// <summary>
    /// Adds a report sink and ensures all registered sinks export at the end of the test run.
    /// The sink is constructed through dependency injection before the optional callback is applied; a
    /// callback supplied by a repeated call is applied too, when the sink is resolved. The same sink
    /// implementation type is registered once, however it was added - including a sink registered
    /// directly in the service collection - and distinct types compose. A sink registered directly
    /// through DI is left to construct itself; repeated calls only configure it.
    /// </summary>
    public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, Action<TSink>? configure = null)
        where TSink : class, IProtoSink
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            RegisterExportHook(services);
            if (FindRegistration(services, typeof(TSink)) is { } existing)
            {
                if (configure is not null)
                {
                    existing.Configures.Add(AddConfigure<TSink>(configure));
                }

                return;
            }

            var directIndex = FindDirectSink(services, typeof(TSink));
            if (directIndex >= 0)
            {
                if (configure is not null)
                {
                    WrapDirectSink(services, directIndex, AddConfigure<TSink>(configure));
                }

                return;
            }

            var registration = new SinkRegistration(typeof(TSink));
            if (configure is not null)
            {
                registration.Configures.Add(AddConfigure<TSink>(configure));
            }

            services.AddSingleton(registration);
            services.AddSingleton<IProtoSink>(serviceProvider =>
            {
                var sink = ActivatorUtilities.CreateInstance<TSink>(serviceProvider);
                ApplyConfigures(sink, registration.Configures);
                if (sink is IProtoConfigurableOptions configurable)
                    configurable.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return sink;
            });
        });
        return builder;
    }

    /// <summary>Adds an existing report sink instance.</summary>
    /// <remarks>
    /// The instance is registered as-is. Unlike the generic overload, it is neither constructed through
    /// dependency injection nor bound from configuration, so the values it was created with are the values
    /// it keeps - a section that exists for the sink's options is ignored. The same sink implementation
    /// type is registered once, however it was added; distinct types compose, and the first registration wins.
    /// </remarks>
    public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, TSink sink)
        where TSink : class, IProtoSink
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sink);
        builder.ConfigureServices(services =>
        {
            RegisterExportHook(services);
            if (FindRegistration(services, sink.GetType()) is not null
                || FindDirectSink(services, sink.GetType()) >= 0)
            {
                return;
            }

            var registration = new SinkRegistration(sink.GetType());
            services.AddSingleton(registration);
            services.AddSingleton<IProtoSink>(_ =>
            {
                ApplyConfigures(sink, registration.Configures);
                return sink;
            });
        });
        return builder;
    }

    private static Action<IProtoSink> AddConfigure<TSink>(Action<TSink> configure)
        where TSink : class, IProtoSink
        => sink => configure((TSink)sink);

    private static void ApplyConfigures(IProtoSink sink, List<Action<IProtoSink>> configures)
    {
        foreach (var configure in configures)
        {
            configure(sink);
        }
    }

    private static SinkRegistration? FindRegistration(IServiceCollection services, Type sinkType)
        => services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(SinkRegistration)
            && descriptor.ImplementationInstance is SinkRegistration registration
            && registration.SinkType == sinkType)?.ImplementationInstance as SinkRegistration;

    /// <summary>
    /// Finds a sink the caller registered directly in the service collection. Only registrations whose
    /// implementation type is known can be recognized; a factory registration is opaque and composes as
    /// its own sink.
    /// </summary>
    private static int FindDirectSink(IServiceCollection services, Type sinkType)
    {
        for (var index = 0; index < services.Count; index++)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != typeof(IProtoSink))
            {
                continue;
            }

            var implementation = descriptor.ImplementationInstance?.GetType() ?? descriptor.ImplementationType;
            if (implementation == sinkType)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Replaces a directly registered sink with an equivalent descriptor that applies the configure
    /// callbacks when the sink is resolved, preserving the original lifetime and construction.
    /// </summary>
    private static void WrapDirectSink(IServiceCollection services, int index, Action<IProtoSink> configure)
    {
        var original = services[index];
        services[index] = new ServiceDescriptor(typeof(IProtoSink), serviceProvider =>
        {
            var sink = original.ImplementationInstance as IProtoSink
                ?? (original.ImplementationFactory is not null
                    ? (IProtoSink)original.ImplementationFactory(serviceProvider)
                    : (IProtoSink)ActivatorUtilities.CreateInstance(serviceProvider, original.ImplementationType!));
            configure(sink);
            return sink;
        }, original.Lifetime);
    }

    private sealed class SinkRegistration(Type sinkType)
    {
        public Type SinkType { get; } = sinkType;

        public List<Action<IProtoSink>> Configures { get; } = [];
    }

    private static void RegisterExportHook(IServiceCollection services)
        => services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoSinkExportHook>());
}
