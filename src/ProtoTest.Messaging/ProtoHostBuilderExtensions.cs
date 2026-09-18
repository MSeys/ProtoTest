namespace ProtoTest.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging.Internal;

public sealed class ProtoMessagingBuilder
{
    internal ProtoMessagingBuilder(IServiceCollection services)
        => Services = services ?? throw new ArgumentNullException(nameof(services));

    public IServiceCollection Services { get; }

    internal Func<IServiceProvider, IProtoMessageBroker>? AdapterFactory { get; private set; }

    /// <summary>Replaces the default in-memory broker with an adapter, for example RabbitMQ.</summary>
    public ProtoMessagingBuilder UseBroker(Func<IServiceProvider, IProtoMessageBroker> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        AdapterFactory = factory;
        return this;
    }
}

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Adds the messaging capability: publish and await messages over a broker. Without an adapter the
    /// run uses the in-memory broker, so the API works anywhere; a real adapter replaces it, registers
    /// the Broker capability and is released with the run as an owned resource.
    /// </summary>
    public static IProtoHostBuilder AddMessaging(
        this IProtoHostBuilder builder,
        Action<ProtoMessagingBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var holder = new BrokerHolder();
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton(serviceProvider =>
            {
                var options = new ProtoMessagingOptions();
                options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return options;
            });

            var messaging = new ProtoMessagingBuilder(services);
            configure?.Invoke(messaging);
            services.TryAddSingleton(holder);

            // A real adapter is what makes the Broker capability true: the in-memory default is a test
            // double, so [RequiresCapability(ProtoCapabilityKinds.Broker)] skips where no broker is
            // reachable and runs where one is, instead of always passing against the double.
            if (messaging.AdapterFactory is { } adapterFactory)
            {
                services.AddSingleton<IProtoMessageBroker>(serviceProvider =>
                {
                    var broker = adapterFactory(serviceProvider);
                    holder.Attach(broker);
                    return broker;
                });
                services.AddSingleton(new ProtoCapabilityDescriptor(
                    "Messaging", ProtoCapabilityKinds.Broker, "ProtoTest.Messaging"));
            }
            else
            {
                services.TryAddSingleton<IProtoMessageBroker>(_ =>
                {
                    var broker = new InMemoryProtoMessageBroker();
                    holder.Attach(broker);
                    return broker;
                });
            }

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoClientInitializer>(
                _ => new ProtoMessageClientInitializer("Default")));
        });

        return builder.AddResource(new ProtoResource(
            "messaging:broker",
            "broker",
            "Messaging broker",
            _ => holder.ReleaseAsync(),
            ProtoResourceScope.Run));
    }
}
