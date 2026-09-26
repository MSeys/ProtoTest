namespace ProtoTest.Core;

using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// Describes one thing the host is composed of: a capability or an adapter behind it. Descriptors are
/// metadata, not lifecycle - they record what an application is made of for the run overview, the trace
/// and the report. Register one with <see cref="ProtoCapabilityExtensions.AddCapability(IProtoHostBuilder, ProtoCapabilityDescriptor)"/>.
/// </summary>
public sealed record ProtoCapabilityDescriptor(string Name, string Kind, string Source)
{
    /// <summary>
    /// Identifies one instance of a capability that can appear several times - the name of an
    /// <c>AddAspNetCoreServer</c> server, an application a device transport belongs to. Two instances
    /// are two capabilities: dropping one does not drop the other. <see langword="null"/> for a
    /// capability that describes the host as a whole, where two equal descriptors are one capability.
    /// <see cref="ProtoHost.HasCapability(string, string?)"/> still matches <see cref="Name"/>, not the
    /// instance; <see cref="ProtoHost.HasCapability(string, string?, string?)"/> can narrow by both.
    /// </summary>
    public string? Instance { get; init; }
}

/// <summary>The capability kinds ProtoTest itself registers; an integration may use its own.</summary>
public static class ProtoCapabilityKinds
{
    public const string Server = "server";
    public const string Worker = "worker";
    public const string Device = "device";
    public const string Protocol = "protocol";
    public const string Browser = "browser";
    public const string Store = "store";
    public const string Broker = "broker";
    public const string Data = "data";
    public const string Document = "document";
}

/// <summary>
/// An order-independent, duplicate-free set of configuration keys with value equality, so two
/// declarations that name the same keys - in any order, however often - compare equal and dedupe.
/// </summary>
internal sealed class ProtoKeySet : IReadOnlyList<string>, IEquatable<ProtoKeySet>
{
    private readonly string[] _keys;

    public ProtoKeySet(IEnumerable<string> keys)
        => _keys = [.. keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)];

    public static ProtoKeySet Empty { get; } = new([]);

    public string this[int index] => _keys[index];

    public int Count => _keys.Length;

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_keys).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _keys.GetEnumerator();

    public bool Equals(ProtoKeySet? other)
        => other is not null && _keys.AsSpan().SequenceEqual(other._keys.AsSpan(), StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ProtoKeySet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in _keys)
        {
            hash.Add(key, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Which environment state makes one capability declaration unnecessary.</summary>
internal enum ProtoCapabilityCondition
{
    /// <summary>The environment already provides what the integration would serve: drop when every key is configured.</summary>
    UnlessConfigured,

    /// <summary>The integration has no address to serve: drop when none of the keys is provided.</summary>
    WhenProvided
}

/// <summary>
/// One capability declaration and the configuration keys that decide it. A declaration without keys is
/// unconditional; one with keys is unnecessary under its <see cref="ProtoCapabilityCondition"/>. Keys
/// are canonicalized, so a repeated registration of the same condition and key set is one declaration.
/// </summary>
internal sealed record ProtoConditionalCapability(
    ProtoCapabilityDescriptor Capability,
    ProtoKeySet Keys,
    ProtoCapabilityCondition Condition = ProtoCapabilityCondition.UnlessConfigured)
{
    public bool IsUnconditional => Condition == ProtoCapabilityCondition.UnlessConfigured && Keys.Count == 0;

    /// <summary>
    /// Whether the run's environment makes this declaration unnecessary. A key counts as provided when
    /// it has a configured value or a registered infrastructure piece declares it - including a piece
    /// the build skips because configuration already provides its keys, so the predicate is the same
    /// before and after the piece starts.
    /// </summary>
    public bool IsDropped(IConfiguration configuration, IReadOnlySet<string> declaredKeys)
        => Condition switch
        {
            ProtoCapabilityCondition.UnlessConfigured =>
                Keys.Count > 0 && Keys.All(key => !string.IsNullOrWhiteSpace(configuration[key])),
            ProtoCapabilityCondition.WhenProvided =>
                Keys.Count > 0 && !Keys.Any(key =>
                    !string.IsNullOrWhiteSpace(configuration[key]) || declaredKeys.Contains(key)),
            _ => false
        };

    /// <summary>The reason recorded when this declaration decides a drop.</summary>
    public string DropReason => Condition switch
    {
        ProtoCapabilityCondition.UnlessConfigured => "already configured",
        ProtoCapabilityCondition.WhenProvided => "no key provided",
        _ => "dropped"
    };
}

/// <summary>
/// A capability the run dropped because no declaration for it is needed: every conditional declaration
/// is unnecessary and no plain declaration promises it. <paramref name="Keys"/> names the configuration
/// keys that decided the skip and <paramref name="Reason"/> why.
/// </summary>
internal sealed record ProtoSkippedCapability(ProtoCapabilityDescriptor Capability, ProtoKeySet Keys, string Reason);

/// <summary>The capabilities the run dropped because no declaration for them is needed.</summary>
internal sealed record ProtoSkippedCapabilities(IReadOnlyList<ProtoSkippedCapability> Capabilities);

/// <summary>
/// A suite-level reason a capability gate reports when it skips and the attribute carries no per-test
/// reason, declared through <c>AddCapabilityReason</c>. <paramref name="Name"/> narrows it to one
/// capability name (or the instance a capability describes, such as a named server); a null name is the
/// reason for the kind as a whole.
/// </summary>
internal sealed record ProtoCapabilityReason(string Kind, string? Name, string Reason);

/// <summary>Registers capability descriptors from the host builder or from an application builder.</summary>
public static class ProtoCapabilityExtensions
{
    /// <summary>Records a capability or adapter the host is composed of.</summary>
    public static IProtoHostBuilder AddCapability(
        this IProtoHostBuilder builder,
        ProtoCapabilityDescriptor capability)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        return builder.ConfigureServices(services => AddCapability(services, capability));
    }

    /// <summary>Records a capability or adapter one application is composed of.</summary>
    public static IProtoApplicationBuilder AddCapability(
        this IProtoApplicationBuilder builder,
        ProtoCapabilityDescriptor capability)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        ThrowIfApplicationBuilderBuilt(builder);
        AddCapability(builder.Services, capability);
        return builder;
    }

    /// <summary>
    /// Records a capability the host is composed of <b>unless</b> every key in
    /// <paramref name="settings"/> already has a configured value. The declaration is then dropped
    /// before the run starts: <see cref="ProtoHost.HasCapability"/> answers false and
    /// <c>[RequiresCapability]</c> skips, exactly as if the integration behind it were never
    /// registered, instead of advertising something the environment provides elsewhere.
    /// </summary>
    /// <remarks>
    /// This is the capability half of the address-provider rule
    /// (<see cref="ProtoInfrastructureExtensions.AddInfrastructure"/>). The decision is made when the
    /// host is built, so it reads configuration, not addresses infrastructure publishes later.
    /// </remarks>
    public static IProtoHostBuilder AddCapabilityUnlessConfigured(
        this IProtoHostBuilder builder,
        ProtoCapabilityDescriptor capability,
        params string[] settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        return builder.ConfigureServices(services => AddConditionalCapability(services, capability, settings ?? []));
    }

    /// <summary>Records a conditional capability one application is composed of.</summary>
    public static IProtoApplicationBuilder AddCapabilityUnlessConfigured(
        this IProtoApplicationBuilder builder,
        ProtoCapabilityDescriptor capability,
        params string[] settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        ThrowIfApplicationBuilderBuilt(builder);
        AddConditionalCapability(builder.Services, capability, settings ?? []);
        return builder;
    }

    /// <summary>
    /// Records a capability the host is composed of <b>only while</b> at least one of
    /// <paramref name="keys"/> can provide it. The declaration is dropped when none of the keys is
    /// provided - no configured value and no registered infrastructure piece declares it - so
    /// <see cref="ProtoHost.HasCapability"/> answers false and <c>[RequiresCapability]</c> skips
    /// instead of advertising an integration whose address cannot exist.
    /// </summary>
    /// <remarks>
    /// This is the address half of the address-provider rule: an integration whose address is missing
    /// stays inert and its capability is absent, so tests skip rather than fail at setup or first use.
    /// The decision is made when the host is built; a key a registered infrastructure piece declares
    /// counts as provided even before the piece starts, including a piece the build skips because
    /// configuration already fills its keys.
    /// </remarks>
    public static IProtoHostBuilder AddCapabilityWhenProvided(
        this IProtoHostBuilder builder,
        ProtoCapabilityDescriptor capability,
        params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        ValidateProvidedKeys(keys);
        return builder.ConfigureServices(services => AddWhenProvided(services, capability, keys));
    }

    /// <summary>Records a provided-capability declaration one application is composed of.</summary>
    public static IProtoApplicationBuilder AddCapabilityWhenProvided(
        this IProtoApplicationBuilder builder,
        ProtoCapabilityDescriptor capability,
        params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        ValidateProvidedKeys(keys);
        ThrowIfApplicationBuilderBuilt(builder);
        AddWhenProvided(builder.Services, capability, keys);
        return builder;
    }

    private static void ValidateProvidedKeys(string[]? keys)
    {
        if (keys is null || !keys.Any(key => !string.IsNullOrWhiteSpace(key)))
        {
            throw new ArgumentException(
                "AddCapabilityWhenProvided requires at least one configuration key; with no key, nothing can provide the capability.",
                nameof(keys));
        }
    }

    /// <summary>
    /// Declares the reason the suite's capability gates of <paramref name="kind"/> report when they
    /// skip, so one sentence covers every gated test instead of repeating on each attribute. Omit
    /// <paramref name="name"/> for the kind as a whole, or set it to narrow the reason to one capability
    /// name (or the instance a capability describes, such as a named server). A gate's own
    /// <c>Reason</c> still overrides it, and a gate whose kind has no declared reason falls back to the
    /// attribute's default message.
    /// </summary>
    /// <remarks>
    /// The reason is resolved when the adapter evaluates the skip, through
    /// <see cref="ProtoHost.FindCapabilityReason(string, string?)"/>. Declare the reason on the host
    /// builder before <see cref="IProtoHostBuilder.Build"/>, like every other composition entry.
    /// </remarks>
    public static IProtoHostBuilder AddCapabilityReason(
        this IProtoHostBuilder builder,
        string kind,
        string reason,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return builder.ConfigureServices(
            services => services.AddSingleton(new ProtoCapabilityReason(kind, name, reason)));
    }

    // Descriptors are metadata by value: registering the same one twice - a helper called twice, two
    // integrations declaring the same adapter - must report one capability, while distinct descriptors
    // of the same CLR type still each register.
    internal static void AddCapability(IServiceCollection services, ProtoCapabilityDescriptor capability)
    {
        AddDescriptor(services, capability);
        AddUnconditionalDeclaration(services, capability);
    }

    private static void AddDescriptor(IServiceCollection services, ProtoCapabilityDescriptor capability)
        => ProtoRegistration.TryAdd(services, capability, existing => existing == capability);

    // A plain declaration is a promise no environment can withdraw: it is what keeps a descriptor a
    // conditional declaration for the same capability also names.
    private static void AddUnconditionalDeclaration(IServiceCollection services, ProtoCapabilityDescriptor capability)
    {
        var declaration = new ProtoConditionalCapability(capability, ProtoKeySet.Empty);
        ProtoRegistration.TryAdd(services, declaration, existing => existing == declaration);
    }

    private static void AddConditionalCapability(
        IServiceCollection services,
        ProtoCapabilityDescriptor capability,
        string[] settings)
    {
        AddDescriptor(services, capability);
        var declaration = new ProtoConditionalCapability(capability, new ProtoKeySet(settings));
        ProtoRegistration.TryAdd(services, declaration, existing => existing == declaration);
    }

    private static void AddWhenProvided(
        IServiceCollection services,
        ProtoCapabilityDescriptor capability,
        string[] keys)
    {
        AddDescriptor(services, capability);
        var declaration = new ProtoConditionalCapability(
            capability,
            new ProtoKeySet(keys),
            ProtoCapabilityCondition.WhenProvided);
        ProtoRegistration.TryAdd(services, declaration, existing => existing == declaration);
    }

    private static void ThrowIfApplicationBuilderBuilt(IProtoApplicationBuilder builder)
    {
        if (builder is IProtoComposableBuilder composable)
        {
            composable.ThrowIfBuilt();
        }
    }
}
