namespace ProtoTest.Core;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

public static class ProtoInfrastructureExtensions
{
    // One target per name on a builder: a repeated registration would be two chains nobody can tell
    // apart in the resolution record. The name is claimed only after the whole chain registered.
    private static readonly ConditionalWeakTable<IProtoHostBuilder, HashSet<string>> RegisteredTargets = new();

    /// <summary>
    /// Registers infrastructure the host starts before the run. Every key in
    /// <paramref name="settings"/> receives the started connection string, so the tests and an
    /// in-process application can read it under their own configuration roots; for a
    /// settings-only piece the keys are the values it will fill, and they decide whether it is needed.
    /// </summary>
    /// <remarks>
    /// The piece is <b>not started</b> when every declared key already has a configured value: the
    /// environment provides those addresses, and starting a container or process would shadow them.
    /// Use <see cref="AddInfrastructureAlways"/> when a piece must start regardless.
    /// <see cref="AddInfrastructure(IProtoHostBuilder, string, Action{IProtoProviderChainBuilder}, string[])"/>
    /// is the replacement: a target's providers in priority order, with
    /// <c>UseConfigured()</c> for the configured-key step-aside and a provider with no condition for a
    /// piece that must start regardless.
    /// </remarks>
    [Obsolete(
        "Use AddInfrastructure(name, chain, keys) with UseConfigured() and a Use(provider) provider " +
        "instead; the old call keeps its all-configured skip rule in 1.x.")]
    public static IProtoHostBuilder AddInfrastructure(
        this IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] settings)
        => AddRunPiece(builder, infrastructure, settings);

    /// <summary>
    /// Registers a target as an ordered provider chain: while the host is built, the providers are
    /// walked in order and the first whose condition holds serves the target. The winner's piece starts
    /// with the run and the winner's capabilities are declared; every other provider is recorded
    /// skipped with the reason it lost, and a target no provider can serve fails the build naming each
    /// candidate.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="name">The target's identity: unique per host, used in records and error messages.</param>
    /// <param name="configure">Adds the target's providers in priority order.</param>
    /// <param name="keys">
    /// The configuration keys the target declares once; every provider checks or fills them. An
    /// application target derives its address key in <c>AddApplication</c> instead of writing it here.
    /// </param>
    public static IProtoHostBuilder AddInfrastructure(
        this IProtoHostBuilder builder,
        string name,
        Action<IProtoProviderChainBuilder> configure,
        params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        // The chain builder hands its providers the collection the target's composition registers
        // into; the shipped builder invokes ConfigureServices immediately, like every integration
        // registration that composes through it.
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        var chain = new ProtoProviderChainBuilder(name, NormalizeKeys(keys), services!);
        configure(chain);
        RegisterTarget(builder, name, chain.Keys, chain.Providers, chain.ResolveAfterTargets);
        return builder;
    }

    /// <summary>
    /// Registers a resolved target once: claims its name, registers each provider's piece with the run
    /// and records the chain. Shared by <see cref="AddInfrastructure(IProtoHostBuilder, string, Action{IProtoProviderChainBuilder}, string[])"/>
    /// and the application builder, which derives its target and key from the application name.
    /// </summary>
    internal static void RegisterTarget(
        IProtoHostBuilder builder,
        string name,
        IReadOnlyList<string> keys,
        IReadOnlyList<IProtoTargetProvider> providers,
        IReadOnlyList<string>? resolveAfter = null)
    {
        var names = RegisteredTargets.GetValue(builder, static _ => new HashSet<string>(StringComparer.Ordinal));
        if (!names.Add(name))
        {
            throw new InvalidOperationException(
                $"A target named '{name}' is already registered on this host builder; add a provider to " +
                "its existing chain instead of registering the target twice.");
        }

        try
        {
            foreach (var provider in providers)
            {
                if (provider.Infrastructure is not null)
                {
                    RegisterChainPiece(builder, provider.Infrastructure, keys);
                }
            }

            builder.ConfigureServices(services => services.AddSingleton(
                new ProtoTargetChain(name, keys, [.. providers])
                {
                    ResolveAfter = resolveAfter ?? []
                }));
        }
        catch
        {
            names.Remove(name);
            throw;
        }
    }

    /// <summary>
    /// Registers infrastructure that starts even when its declared keys are already configured.
    /// </summary>
    public static IProtoHostBuilder AddInfrastructureAlways(
        this IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] settings)
        => Register(builder, infrastructure, alwaysStart: true, settings);

    // The framework's own convenience registrations - run setup and readiness - keep the
    // all-configured skip rule without going through the obsolete compatibility overload.
    internal static IProtoHostBuilder AddRunPiece(
        this IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] settings)
        => Register(builder, infrastructure, alwaysStart: false, settings);

    /// <summary>
    /// Registers a run-scoped setup step: an action the host runs once when the run starts, at this
    /// registration's position in the infrastructure order. Register it after the pieces whose
    /// published settings it reads - a container's connection string, a broker's address - so it sees
    /// their started values; the run's cancellation token arrives through
    /// <see cref="ProtoRunSetupContext.CancellationToken"/>.
    /// </summary>
    /// <remarks>
    /// A step is an action, not a resource: it owns nothing to release, so the run's stop and dispose
    /// do not run it again. A step that throws fails the run's start with its own exception, releases
    /// what the run had started and leaves the host retryable, exactly like failing infrastructure; a
    /// retry runs the step again. Use it for state the whole run shares - a schema for a container
    /// database - because a run hook runs before infrastructure starts and cannot see its addresses,
    /// and a test hook or test body runs inside the per-test transaction, where its DDL is rolled back
    /// with the test.
    /// </remarks>
    public static IProtoHostBuilder AddRunSetup(
        this IProtoHostBuilder builder,
        string name,
        Func<ProtoRunSetupContext, ValueTask> setup)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(setup);
        return builder.AddRunPiece(new ProtoRunSetupInfrastructure(name, setup));
    }

    /// <summary>
    /// Registers a chain provider's piece with the run: it starts at this registration position when it
    /// wins its target, and the target's resolution - not the all-configured rule - decides whether it
    /// is needed.
    /// </summary>
    internal static void RegisterChainPiece(
        IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        IReadOnlyList<string> keys)
        => Register(builder, infrastructure, alwaysStart: false, settings: [.. keys], chainControlled: true);

    private static IProtoHostBuilder Register(
        IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        bool alwaysStart,
        string[]? settings,
        bool chainControlled = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(infrastructure);
        var keys = settings ?? [];
        if (infrastructure.Scope != ProtoResourceScope.Run)
        {
            throw new ArgumentException(
                $"{infrastructure.GetType().Name} must be run-scoped (ProtoResourceScope.Run); infrastructure outlives a test.",
                nameof(infrastructure));
        }

        if (keys.Length > 0 && infrastructure is not (IProtoConnectionInfrastructure or IProtoSettingsInfrastructure))
        {
            throw new ArgumentException(
                $"{infrastructure.GetType().Name} does not provide an address, so it cannot declare configuration keys.",
                nameof(settings));
        }

        // Validate and reserve the resource before touching the service collection: a conflicting
        // instance must fail the whole registration without leaving half-applied settings behind.
        builder.AddResource(infrastructure);

        builder.ConfigureServices(services => AddRegistration(services, infrastructure, keys, alwaysStart, chainControlled));
        return builder;
    }

    // Adding the same infrastructure twice is one lifecycle, but every call's keys count: the repeated
    // registration merges its settings into the existing one so both roots get filled. A chain
    // registration marks the piece chain-controlled whichever call it came from.
    private static void AddRegistration(
        IServiceCollection services,
        IProtoInfrastructure infrastructure,
        string[] keys,
        bool alwaysStart,
        bool chainControlled)
    {
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index] is not
                { ServiceType: var serviceType, ImplementationInstance: ProtoInfrastructureRegistration existing }
                || serviceType != typeof(ProtoInfrastructureRegistration)
                || !string.Equals(existing.Infrastructure.Id, infrastructure.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var merged = existing.Settings.Concat(keys).Distinct(StringComparer.Ordinal).ToArray();
            services[index] = ServiceDescriptor.Singleton(
                new ProtoInfrastructureRegistration(
                    existing.Infrastructure,
                    merged,
                    existing.AlwaysStart || alwaysStart,
                    existing.ChainControlled || chainControlled));
            return;
        }

        services.AddSingleton(new ProtoInfrastructureRegistration(infrastructure, [.. keys], alwaysStart, chainControlled));
    }

    // The target's identity: blank entries never reach the chain, and one key declared twice is one key.
    private static string[] NormalizeKeys(string[]? keys)
        => [.. (keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal)];
}
