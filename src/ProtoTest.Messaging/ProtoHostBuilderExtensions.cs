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

    /// <summary>
    /// The configuration keys the configured adapter reads its address from; empty for an adapter that
    /// does not declare them, which keeps the <c>Broker</c> capability unconditional.
    /// </summary>
    internal IReadOnlyList<string> BrokerAddressKeys { get; private set; } = [];

    /// <summary>
    /// Replaces the default in-memory broker with an adapter, for example RabbitMQ. The broker the
    /// factory returns is owned by ProtoTest: it is released with the run.
    /// </summary>
    public ProtoMessagingBuilder UseBroker(Func<IServiceProvider, IProtoMessageBroker> factory)
        => UseBroker(factory, []);

    /// <summary>
    /// Replaces the default in-memory broker with an adapter that reads its address from
    /// <paramref name="addressKeys"/>. The <c>Broker</c> capability is then declared only while at
    /// least one key can provide an address - a configured value, or a key registered infrastructure
    /// declares and fills when it starts - so a run without one skips instead of failing at setup or
    /// first use. An adapter that provides its address without configuration keys keeps the
    /// unconditional declaration. The broker the factory returns is owned by ProtoTest: it is released
    /// with the run.
    /// </summary>
    public ProtoMessagingBuilder UseBroker(
        Func<IServiceProvider, IProtoMessageBroker> factory,
        params string[] addressKeys)
    {
        ArgumentNullException.ThrowIfNull(factory);
        AdapterFactory = factory;
        BrokerAddressKeys = addressKeys ?? [];
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

    /// <summary>
    /// Declares the destinations this suite taps, in code, so the adapter can bind each test's tap
    /// during setup rather than at the first await. Repeated calls compose; configuration under
    /// <c>ProtoTest:Messaging:Destinations</c> still applies over these values, so an environment can
    /// add its own.
    /// </summary>
    /// <remarks>
    /// <c>Tap</c> is a reliability declaration, not just a convenience: pre-bind every destination the
    /// act publishes to. A destination declared only at the first <c>AwaitAsync</c> is bound then, so it
    /// misses every message published before that await.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destinations"/> is empty, or contains a null, empty or whitespace destination.
    /// </exception>
    public ProtoMessagingBuilder Tap(params string[] destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        if (destinations.Length == 0)
        {
            throw new ArgumentException("Provide at least one destination.", nameof(destinations));
        }

        foreach (var destination in destinations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        }

        // An options callback, like every other messaging option: repeated calls compose in order and
        // the configuration section binds over the result, so the environment can add destinations.
        ProtoOptionsRegistration.Configure(Services, () => new MessagingOptions(), options =>
        {
            foreach (var destination in destinations)
            {
                if (!options.Destinations.Contains(destination, StringComparer.Ordinal))
                {
                    options.Destinations.Add(destination);
                }
            }
        });
        return this;
    }
}

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Adds the messaging capability: publish and await messages over a broker. Without an adapter the
    /// run uses the in-memory broker, so the API works anywhere; a real adapter replaces it, registers
    /// the Broker capability and is released with the run as an owned resource. An adapter that
    /// declares its address keys (see
    /// <see cref="ProtoMessagingBuilder.UseBroker(Func{IServiceProvider, IProtoMessageBroker}, string[])"/>)
    /// declares the capability conditionally: it is absent while no key can provide the address, so
    /// <c>[RequiresCapability(ProtoCapabilityKinds.Broker)]</c> skips instead of failing.
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

            if (!registration.Configure(services, messaging.AdapterFactory))
            {
                return;
            }

            // A real adapter is what makes the Broker capability true: the in-memory default is a test
            // double, so [RequiresCapability(ProtoCapabilityKinds.Broker)] skips where no broker is
            // reachable and runs where one is, instead of always passing against the double. An
            // adapter that names its address keys is honest in both directions: a run with neither a
            // configured value nor a piece that declares one drops the capability and skips.
            var capability = new ProtoCapabilityDescriptor(
                ProtoMessagingProtocol.Protocol.Name, ProtoCapabilityKinds.Broker, ProtoMessagingProtocol.Protocol.TraceSource);
            if (messaging.BrokerAddressKeys.Count == 0)
            {
                builder.AddCapability(capability);
            }
            else
            {
                builder.AddCapabilityWhenProvided(capability, [.. messaging.BrokerAddressKeys]);
            }
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
        /// later call can supply one; the first adapter configured wins. Returns whether this call's
        /// adapter is the one that will serve, so the caller declares the capability for it once.
        /// </summary>
        public bool Configure(IServiceCollection services, Func<IServiceProvider, IProtoMessageBroker>? adapterFactory)
        {
            if (adapterFactory is not null)
            {
                if (_adapterConfigured)
                {
                    return false;
                }

                _adapterConfigured = true;
                services.RemoveAll<IProtoMessageBroker>();
                services.AddSingleton<IProtoMessageBroker>(serviceProvider =>
                {
                    var broker = adapterFactory(serviceProvider);
                    Holder.Attach(broker);
                    return broker;
                });
                return true;
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

            return false;
        }
    }
}
