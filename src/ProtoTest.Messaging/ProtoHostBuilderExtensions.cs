namespace ProtoTest.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>
    /// Enables automatic published and received payload attachments, sanitized and redacted. Repeated
    /// calls compose: every callback runs in registration order and configuration binds over the result.
    /// </summary>
    public ProtoMessagingBuilder CaptureAttachments(Action<MessagingAttachmentOptions>? configure = null)
    {
        ProtoOptionsRegistration.Configure(Services, () => new MessagingAttachmentOptions(), configure);
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
    /// <remarks>
    /// A repeated call is not a no-op: its <c>configure</c> callback always runs, so a later call can add
    /// an adapter to an adapter-less first call or extend attachment options. Infrastructure is
    /// idempotent (one holder, initializer, capability and run resource) and the first adapter wins; a
    /// call whose configure throws leaves no guard behind, so a later successful call still composes.
    /// </remarks>
    public static IProtoHostBuilder AddMessaging(
        this IProtoHostBuilder builder,
        Action<ProtoMessagingBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            // The configure callback runs first: a call whose configure throws must leave no
            // registration behind, so a later successful call still composes.
            var messaging = new ProtoMessagingBuilder(services);
            configure?.Invoke(messaging);

            var registration = MessagingRegistration.Add(services, out var created);
            if (created)
            {
                builder.AddResource(new ProtoResource(
                    "messaging:broker",
                    "broker",
                    "Messaging broker",
                    _ => registration.Holder.ReleaseAsync(),
                    ProtoResourceScope.Run));
            }

            registration.Configure(services, messaging.AdapterFactory);
        });
        return builder;
    }

    private sealed class MessagingRegistration
    {
        public BrokerHolder Holder { get; } = new();

        private bool _adapterConfigured;

        /// <summary>Adds the registration once and reports whether this call created it.</summary>
        public static MessagingRegistration Add(IServiceCollection services, out bool created)
        {
            var existing = services
                .LastOrDefault(descriptor => descriptor.ServiceType == typeof(MessagingRegistration))?
                .ImplementationInstance as MessagingRegistration;
            created = existing is null;
            var registration = existing ?? new MessagingRegistration();
            if (!created)
            {
                return registration;
            }

            services.AddSingleton(registration);
            services.AddSingleton<IProtoClientInitializer>(_ => new ProtoMessageClientInitializer("Default"));
            services.TryAddSingleton(registration.Holder);
            services.TryAddSingleton(serviceProvider =>
                ProtoOptionsRegistration.Resolve<MessagingOptions>(serviceProvider));
            return registration;
        }

        /// <summary>
        /// Applies this call's adapter, if any. An adapter always replaces the in-memory default, so a
        /// later call can supply one; the first adapter configured wins.
        /// </summary>
        public void Configure(IServiceCollection services, Func<IServiceProvider, IProtoMessageBroker>? adapterFactory)
        {
            if (adapterFactory is not null)
            {
                if (_adapterConfigured)
                {
                    return;
                }

                _adapterConfigured = true;
                services.RemoveAll<IProtoMessageBroker>();
                services.AddSingleton<IProtoMessageBroker>(serviceProvider =>
                {
                    var broker = adapterFactory(serviceProvider);
                    Holder.Attach(broker);
                    return broker;
                });
                services.AddSingleton(new ProtoCapabilityDescriptor(
                    ProtoMessagingProtocol.Protocol.Name, ProtoCapabilityKinds.Broker, ProtoMessagingProtocol.Protocol.TraceSource));
                return;
            }

            // A real adapter is what makes the Broker capability true: the in-memory default is a test
            // double, so [RequiresCapability(ProtoCapabilityKinds.Broker)] skips where no broker is
            // reachable and runs where one is, instead of always passing against the double.
            if (!_adapterConfigured)
            {
                services.TryAddSingleton<IProtoMessageBroker>(_ =>
                {
                    var broker = new InMemoryProtoMessageBroker();
                    Holder.Attach(broker);
                    return broker;
                });
            }
        }
    }
}
