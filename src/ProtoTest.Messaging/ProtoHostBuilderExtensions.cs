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

    /// <summary>Replaces the default in-memory broker with an adapter, for example RabbitMQ.</summary>
    public ProtoMessagingBuilder UseBroker(Func<IServiceProvider, IProtoMessageBroker> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.RemoveAll<IProtoMessageBroker>();
        Services.AddSingleton(factory);
        return this;
    }
}

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Adds the messaging capability: publish and await messages over a broker. Without an adapter the
    /// run uses the in-memory broker, so the API works anywhere; a real adapter replaces it.
    /// </summary>
    public static IProtoHostBuilder AddMessaging(
        this IProtoHostBuilder builder,
        Action<ProtoMessagingBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton(serviceProvider =>
            {
                var options = new ProtoMessagingOptions();
                options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return options;
            });
            configure?.Invoke(new ProtoMessagingBuilder(services));

            // A real adapter is what makes the Broker capability true: the in-memory default is a test
            // double, so [RequiresCapability(ProtoCapabilityKinds.Broker)] skips where no broker is
            // reachable and runs where one is, instead of always passing against the double.
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IProtoMessageBroker)))
            {
                services.AddSingleton(new ProtoCapabilityDescriptor(
                    "Messaging", ProtoCapabilityKinds.Broker, "ProtoTest.Messaging"));
            }

            services.TryAddSingleton<IProtoMessageBroker>(_ => new InMemoryProtoMessageBroker());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoClientInitializer>(
                _ => new ProtoMessageClientInitializer("Default")));
        });
        return builder;
    }
}
