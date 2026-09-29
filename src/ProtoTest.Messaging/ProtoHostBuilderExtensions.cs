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
    /// The application whose provider chain decides the <c>Broker</c> capability, when the adapter
    /// serves only while that application runs in-process; null for an adapter that resolves its own
    /// address.
    /// </summary>
    internal string? BrokerInProcessApplication { get; private set; }

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
        BrokerInProcessApplication = null;
        return this;
    }

    /// <summary>
    /// Replaces the default in-memory broker with an adapter that serves only while
    /// <paramref name="application"/> is hosted in-process - an in-process resource whose harness or
    /// listener exists only in this process. The <c>Broker</c> capability is declared only while the
    /// application's provider chain winner runs it in-process, so a loopback, container or AppHost
    /// application drops it and tests skip instead of advertising an adapter that cannot serve; a host
    /// whose application declares no chain keeps the configured-keys rule over
    /// <paramref name="configuredKeys"/>. The MassTransit bridge over the application's in-process test
    /// harness is the example. The broker the factory returns is owned by ProtoTest: it is released
    /// with the run.
    /// </summary>
    public ProtoMessagingBuilder UseBrokerWhenInProcess(
        Func<IServiceProvider, IProtoMessageBroker> factory,
        string application,
        params string[] configuredKeys)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        AdapterFactory = factory;
        BrokerAddressKeys = configuredKeys ?? [];
        BrokerInProcessApplication = application;
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
    /// during setup rather than at the first await. A destination is an exchange name or a queue
    /// destination (<see cref="ProtoDestination.Queue"/>, consumed directly by an adapter that owns
    /// queues). Repeated calls compose; configuration under <c>ProtoTest:Messaging:Destinations</c>
    /// still applies over these values, so an environment can add its own.
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

    /// <summary>
    /// Declares the destinations this suite owns, in code, so the adapter creates them on the broker
    /// during test setup - before any tap binds and before the act publishes. A suite that owns the
    /// broker and publishes its own events uses <c>Declare</c> for the destinations nothing else
    /// declares; a destination the application declares stays the application's. Repeated calls
    /// compose and skip a value already declared; configuration under
    /// <c>ProtoTest:Messaging:DeclaredDestinations</c> still applies over these values. Declaration is
    /// idempotent: an adapter leaves an existing destination as it is, a destination is declared once
    /// per run, and a repeated declaration is a no-op.
    /// </summary>
    /// <remarks>
    /// The declaration is configuration, not a runtime call: the builder is consumed when
    /// <c>AddMessaging</c> runs, so a destination cannot be declared after a test has prepared. An
    /// adapter whose broker has no topology - the in-memory broker, where every destination already
    /// exists - treats <c>Declare</c> as a no-op. A queue destination is consumed, not declared: the
    /// component that owns the queue creates it, so <c>Declare</c> refuses the queue form.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destinations"/> is empty, or contains a null, empty or whitespace destination,
    /// or a queue destination.
    /// </exception>
    public ProtoMessagingBuilder Declare(params string[] destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        if (destinations.Length == 0)
        {
            throw new ArgumentException("Provide at least one destination.", nameof(destinations));
        }

        foreach (var destination in destinations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
            if (ProtoDestination.IsQueue(destination))
            {
                throw new ArgumentException(
                    $"'{destination}' addresses a queue, and Declare creates the suite's exchanges. The " +
                    "component that owns the queue (the application's topology, or the dead-letter " +
                    "bindings that feed it) creates it; await it with a queue destination instead.",
                    nameof(destinations));
            }
        }

        // The same options callback shape as Tap: repeated calls compose in order and the configuration
        // section binds over the result, so the environment can add declarations.
        ProtoOptionsRegistration.Configure(Services, () => new MessagingOptions(), options =>
        {
            foreach (var destination in destinations)
            {
                if (!options.DeclaredDestinations.Contains(destination, StringComparer.Ordinal))
                {
                    options.DeclaredDestinations.Add(destination);
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
    /// <c>[RequiresCapability(ProtoCapabilityKinds.Broker)]</c> skips instead of failing. An adapter
    /// that serves only behind the in-process test host
    /// (see <see cref="ProtoMessagingBuilder.UseBrokerWhenInProcess"/>) declares it the other way: the
    /// application's provider chain winner decides, and a host without a chain keeps the
    /// configured-keys rule.
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
            // configured value nor a piece that declares one drops the capability and skips, and an
            // in-process adapter follows the served application's provider chain, which drops it for a
            // loopback, container or AppHost winner.
            var capability = new ProtoCapabilityDescriptor(
                ProtoMessagingProtocol.Protocol.Name, ProtoCapabilityKinds.Broker, ProtoMessagingProtocol.Protocol.TraceSource);
            if (messaging.BrokerInProcessApplication is { } application)
            {
                builder.AddCapabilityWhenInProcess(application, capability, [.. messaging.BrokerAddressKeys]);
            }
            else if (messaging.BrokerAddressKeys.Count == 0)
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
