namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// A run piece that can serve a target - an application's address, a store, a broker or a worker. A
/// target registers its providers in priority order; the first provider whose condition holds serves
/// it, so only that provider's piece starts and only that provider's capabilities are declared.
/// </summary>
/// <remarks>
/// A provider is the extension point behind <c>AddInfrastructure(name, chain, keys)</c>: a third party
/// writes one by implementing this interface or by constructing <see cref="ProtoTargetProvider"/>, and
/// the framework resolves it without knowing the environment it describes. Conditions are evaluated
/// against the resolved configuration while the host is built, before any piece starts.
/// </remarks>
public interface IProtoTargetProvider
{
    /// <summary>Gets the name that identifies the provider in the resolution record and skip reasons.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the condition under which this provider can serve the target; <see langword="null"/> means
    /// the provider is always available.
    /// </summary>
    IProtoProviderCondition? Condition { get; }

    /// <summary>
    /// Gets the piece that serves the target when this provider wins - started with the run and
    /// released with it, filling the target's declared keys. <see langword="null"/> when configuration
    /// itself serves the target, so the provider starts nothing.
    /// </summary>
    IProtoInfrastructure? Infrastructure { get; }

    /// <summary>
    /// Gets the capabilities the provider serves. Only the winning provider's capabilities are
    /// declared, so a losing provider never advertises something the run cannot do.
    /// </summary>
    IReadOnlyList<ProtoCapabilityDescriptor> Capabilities => [];

    /// <summary>
    /// Contributes the services that exist only while this provider serves its target - the client
    /// initializers, transports and per-target registrations another provider's process cannot supply.
    /// Called while the host is built, for the winning provider only, so a losing provider leaves no
    /// service behind.
    /// </summary>
    void ConfigureServices(IServiceCollection services)
    {
    }
}

/// <summary>
/// A provider described by its parts: a name, the piece that serves the target, the condition, the
/// capabilities it serves and the services the winner contributes. Use it when the provider has no
/// behavior beyond those parts; implement <see cref="IProtoTargetProvider"/> when it does.
/// </summary>
/// <param name="Name">The provider's name in the resolution record and skip reasons.</param>
/// <param name="Infrastructure">The piece that serves the target when this provider wins; none when configuration itself serves it.</param>
/// <param name="Condition">When the provider can serve the target; none means always.</param>
public sealed record ProtoTargetProvider(
    string Name,
    IProtoInfrastructure? Infrastructure = null,
    IProtoProviderCondition? Condition = null) : IProtoTargetProvider
{
    /// <inheritdoc />
    public IReadOnlyList<ProtoCapabilityDescriptor> Capabilities { get; init; } = [];

    /// <summary>
    /// The services the provider contributes when it wins its target, in the
    /// <see cref="IProtoTargetProvider.ConfigureServices"/> contract. <see langword="null"/> leaves
    /// the winner with no services of its own.
    /// </summary>
    public Action<IServiceCollection>? WinnerServices { get; init; }

    void IProtoTargetProvider.ConfigureServices(IServiceCollection services)
        => WinnerServices?.Invoke(services);
}

/// <summary>The resolved environment a provider condition is evaluated against.</summary>
/// <param name="Configuration">The run's configuration, resolved when the host was built.</param>
/// <param name="Keys">The configuration keys the target declares, once for the whole chain.</param>
/// <param name="ResolvedTargets">
/// The targets resolved before this chain, keyed by target name. A chain that declared
/// <see cref="IProtoProviderChainBuilder.ResolveAfter"/> reads the winners of those targets here.
/// </param>
public sealed record ProtoProviderConditionContext(
    IConfiguration Configuration,
    IReadOnlyList<string> Keys,
    IReadOnlyDictionary<string, ProtoResolvedTarget>? ResolvedTargets = null)
{
    /// <summary>Returns the resolution of a target resolved before this chain, or <see langword="null"/>.</summary>
    public ProtoResolvedTarget? Target(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return ResolvedTargets is not null && ResolvedTargets.TryGetValue(targetName, out var target)
            ? target
            : null;
    }
}

/// <summary>
/// How one target resolved: the winning provider and the capabilities it declared. A dependent chain
/// reads it from <see cref="ProtoProviderConditionContext"/> to follow the target it belongs to.
/// </summary>
/// <param name="TargetName">The target's identity.</param>
/// <param name="ProviderName">The name of the provider that won the chain.</param>
/// <param name="Capabilities">The capabilities the winning provider declared.</param>
public sealed record ProtoResolvedTarget(
    string TargetName,
    string ProviderName,
    IReadOnlyList<ProtoCapabilityDescriptor> Capabilities)
{
    /// <summary>Whether the winning provider declared a capability of the given kind.</summary>
    public bool HasCapability(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return Capabilities.Any(capability => string.Equals(capability.Kind, kind, StringComparison.Ordinal));
    }
}

/// <summary>
/// When one provider can serve its target. Conditions are evaluated in chain order against the resolved
/// configuration; the first that holds wins, so the provider order is the priority of the environments
/// the chain describes.
/// </summary>
public interface IProtoProviderCondition
{
    /// <summary>Returns whether the condition holds in the given environment.</summary>
    bool IsSatisfied(ProtoProviderConditionContext context);

    /// <summary>
    /// Describes what must hold for the condition to be satisfied; the resolution record and the
    /// no-provider error use it to name every unmet condition. Only called when the condition failed.
    /// </summary>
    string Describe(ProtoProviderConditionContext context);
}

/// <summary>
/// The canonical provider conditions: <see cref="Configured"/> for a target the environment already
/// provides, <see cref="Selected"/> for an integration-owned selection key, <see cref="Available"/> for
/// a runtime probe, and <see cref="Always"/> for the fallback a chain ends with.
/// </summary>
public static class ProtoProviderConditions
{
    private static readonly IProtoProviderCondition ConfiguredCondition = new ProtoConfiguredCondition();
    private static readonly IProtoProviderCondition AlwaysCondition = new ProtoAlwaysCondition();

    /// <summary>
    /// Holds when every configuration key the target declares has a value: the environment already
    /// provides what the provider would fill.
    /// </summary>
    public static IProtoProviderCondition Configured => ConfiguredCondition;

    /// <summary>
    /// Holds when the integration-owned selection key has a value - a convention the run script sets as
    /// an environment variable, never a parameter of the provider itself.
    /// </summary>
    public static IProtoProviderCondition Selected(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new ProtoSelectedCondition([key]);
    }

    /// <summary>
    /// Holds when any of the integration-owned selection keys has a value, so a provider with a broad
    /// switch and a per-target switch serves when either is set. Every key is a convention the run
    /// script sets as an environment variable, never a parameter of the provider itself.
    /// </summary>
    public static IProtoProviderCondition Selected(string key, params string[] moreKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (moreKeys is null || moreKeys.Length == 0)
        {
            return new ProtoSelectedCondition([key]);
        }

        var keys = new List<string>(moreKeys.Length + 1) { key };
        foreach (var moreKey in moreKeys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(moreKey);
            if (!keys.Contains(moreKey, StringComparer.Ordinal))
            {
                keys.Add(moreKey);
            }
        }

        return new ProtoSelectedCondition(keys);
    }

    /// <summary>
    /// Holds when the runtime probe succeeds. <paramref name="requirement"/> names what the probe checks
    /// for the skip record and the no-provider error, for example "Docker is available". The probe runs
    /// once per resolution; an exception it throws propagates - a probe that cannot answer is a provider
    /// bug, not an unavailable environment.
    /// </summary>
    public static IProtoProviderCondition Available(string requirement, Func<bool> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        ArgumentNullException.ThrowIfNull(probe);
        return new ProtoAvailableCondition(requirement, probe);
    }

    /// <summary>Holds everywhere: the fallback provider a chain ends with.</summary>
    public static IProtoProviderCondition Always => AlwaysCondition;

    private sealed class ProtoConfiguredCondition : IProtoProviderCondition
    {
        public bool IsSatisfied(ProtoProviderConditionContext context)
            => ProtoEnvironment.IsSatisfied(
                context.Configuration,
                context.Keys,
                ProtoEnvironmentMode.AllConfigured,
                ProtoEnvironment.NoDeclaredKeys);

        public string Describe(ProtoProviderConditionContext context)
        {
            if (context.Keys.Count == 0)
            {
                return "The target declares no configuration key, so no configured value can serve it";
            }

            var missing = context.Keys
                .Where(key => !ProtoEnvironment.HasValue(context.Configuration, key))
                .ToArray();
            return missing.Length == 0
                ? "Every key the target declares has a configured value"
                : $"Every key the target declares must have a configured value (missing: {string.Join(", ", missing)})";
        }
    }

    private sealed class ProtoSelectedCondition(IReadOnlyList<string> keys) : IProtoProviderCondition
    {
        public bool IsSatisfied(ProtoProviderConditionContext context)
            => keys.Any(key => ProtoEnvironment.HasValue(context.Configuration, key));

        public string Describe(ProtoProviderConditionContext context)
            => keys.Count == 1
                ? $"The selection key '{keys[0]}' must be set"
                : $"One of the selection keys must be set: {string.Join(", ", keys.Select(key => $"'{key}'"))}";
    }

    private sealed class ProtoAvailableCondition(string requirement, Func<bool> probe) : IProtoProviderCondition
    {
        public bool IsSatisfied(ProtoProviderConditionContext context) => probe();

        public string Describe(ProtoProviderConditionContext context) => requirement;
    }

    private sealed class ProtoAlwaysCondition : IProtoProviderCondition
    {
        public bool IsSatisfied(ProtoProviderConditionContext context) => true;

        public string Describe(ProtoProviderConditionContext context) => "Always available";
    }
}

/// <summary>
/// Builds one target's provider chain. Providers are added in priority order: the first whose condition
/// holds serves the target, and every later provider is recorded skipped with the reason.
/// </summary>
public interface IProtoProviderChainBuilder
{
    /// <summary>Gets the target's name, used to identify the chain in records and error messages.</summary>
    string TargetName { get; }

    /// <summary>Gets the configuration keys the target declares once; every provider checks or fills them.</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>Gets the service collection this target's composition registers into.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Resolves this target after the named targets, so a condition on this chain can read their
    /// winners through <see cref="ProtoProviderConditionContext.Target"/>. A worker nested under an
    /// application resolves after it: whether the run hosts it depends on how the application is
    /// served. Targets resolved before this chain are also visible without it; this only orders one
    /// chain behind another whose own chain would otherwise resolve later.
    /// </summary>
    IProtoProviderChainBuilder ResolveAfter(params string[] targetNames);

    /// <summary>Adds a provider to the end of the chain; a later provider is lower priority.</summary>
    IProtoProviderChainBuilder Use(IProtoTargetProvider provider);

    /// <summary>
    /// Adds the provider that serves from configured values: it holds when every key the target
    /// declares has one, and it starts nothing.
    /// </summary>
    IProtoProviderChainBuilder UseConfigured();
}

/// <summary>
/// Thrown when a target's provider chain cannot serve it - no provider is registered, or no provider's
/// condition holds. The message names the target and every provider's unmet condition, so a suite fixes
/// its composition instead of failing at first use.
/// </summary>
public sealed class ProtoTargetResolutionException : InvalidOperationException
{
    /// <summary>Creates the failure for one target that no provider can serve.</summary>
    public ProtoTargetResolutionException(string targetName, string message)
        : base(message)
        => TargetName = targetName;

    /// <summary>Gets the name of the target no provider can serve.</summary>
    public string TargetName { get; }
}

/// <summary>The trace vocabulary a target's resolution records.</summary>
public static class ProtoTargetTrace
{
    /// <summary>
    /// One event per target: the target, its keys, the winning provider and every skipped provider with
    /// the reason it lost.
    /// </summary>
    public const string Resolved = "environment.resolved";

    /// <summary>One event per provider that did not win its target's chain, carrying the reason.</summary>
    public const string ProviderSkipped = "environment.provider.skipped";
}
