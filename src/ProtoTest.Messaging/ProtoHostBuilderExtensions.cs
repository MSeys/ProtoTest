namespace ProtoTest.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging.Internal;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// Enables automatic published and received payload attachments, sanitized and redacted. Code
    /// configuration runs first and the known configuration section binds over it afterwards.
    /// </summary>
    public ProtoMessagingBuilder CaptureAttachments(Action<MessagingAttachmentOptions>? configure = null)
    {
        Services.RemoveAll<MessagingAttachmentOptions>();
        Services.TryAddSingleton(serviceProvider =>
        {
            var options = new MessagingAttachmentOptions();
            configure?.Invoke(options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }
}

public static class ProtoHostBuilderExtensions
{
    private static readonly ConditionalWeakTable<IProtoHostBuilder, MessagingRegistration> Registrations = new();

    /// <summary>
    /// Adds the messaging capability: publish and await messages over a broker. Without an adapter the
    /// run uses the in-memory broker, so the API works anywhere; a real adapter replaces it, registers
    /// the Broker capability and is released with the run as an owned resource.
    /// </summary>
    /// <remarks>
    /// A repeated call is not a no-op: its <c>configure</c> callback always runs, so a later call can add
    /// an adapter to an adapter-less first call or refresh attachment options. Infrastructure is
    /// idempotent (one holder, initializer, capability and run resource) and the first adapter wins; a
    /// call whose configure throws leaves no guard behind, so a later successful call still composes.
    /// </remarks>
    public static IProtoHostBuilder AddMessaging(
        this IProtoHostBuilder builder,
        Action<ProtoMessagingBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var registration = Registrations.GetValue(builder, static _ => new MessagingRegistration());
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton(serviceProvider =>
            {
                var options = new MessagingOptions();
                options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
                return options;
            });

            services.TryAddSingleton(registration.Holder);
            var messaging = new ProtoMessagingBuilder(services);
            configure?.Invoke(messaging);

            if (messaging.AdapterFactory is { } adapterFactory)
            {
                // An adapter always replaces the in-memory default, so a later call can supply one; the
                // first adapter configured wins, mirroring the same-name rule the protocols use.
                if (!registration.AdapterConfigured)
                {
                    registration.AdapterConfigured = true;
                    services.RemoveAll<IProtoMessageBroker>();
                    services.AddSingleton<IProtoMessageBroker>(serviceProvider =>
                    {
                        var broker = adapterFactory(serviceProvider);
                        registration.Holder.Attach(broker);
                        return broker;
                    });
                    services.AddSingleton(new ProtoCapabilityDescriptor(
                        "Messaging", ProtoCapabilityKinds.Broker, "ProtoTest.Messaging"));
                }
            }
            else if (!registration.AdapterConfigured)
            {
                // A real adapter is what makes the Broker capability true: the in-memory default is a test
                // double, so [RequiresCapability(ProtoCapabilityKinds.Broker)] skips where no broker is
                // reachable and runs where one is, instead of always passing against the double.
                services.TryAddSingleton<IProtoMessageBroker>(_ =>
                {
                    var broker = new InMemoryProtoMessageBroker();
                    registration.Holder.Attach(broker);
                    return broker;
                });
            }

            services.TryAddSingleton<IProtoClientInitializer>(
                _ => new ProtoMessageClientInitializer("Default"));
        });

        if (Interlocked.Exchange(ref registration.ResourceRegistered, 1) == 0)
        {
            builder.AddResource(new ProtoResource(
                "messaging:broker",
                "broker",
                "Messaging broker",
                _ => registration.Holder.ReleaseAsync(),
                ProtoResourceScope.Run));
        }

        return builder;
    }

    private sealed class MessagingRegistration
    {
        public BrokerHolder Holder { get; } = new();
        public bool AdapterConfigured { get; set; }
        public int ResourceRegistered;
    }
}
