namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Describes one thing the host is composed of: a capability or an adapter behind it. Descriptors are
/// metadata, not lifecycle - they record what an application is made of for the run overview, the trace
/// and the report. Register one with <see cref="ProtoCapabilityExtensions.AddCapability(IProtoHostBuilder, ProtoCapabilityDescriptor)"/>.
/// </summary>
public sealed record ProtoCapabilityDescriptor(string Name, string Kind, string Source);

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
/// One capability declaration and the configuration keys that can make it unnecessary. A declaration
/// without keys is unconditional; one with keys is dropped when every key is configured.
/// </summary>
internal sealed record ProtoConditionalCapability(
    ProtoCapabilityDescriptor Capability,
    IReadOnlyList<string> Keys)
{
    public bool IsSatisfiedBy(IConfiguration configuration)
        => Keys.Count > 0
           && Keys.All(key => !string.IsNullOrWhiteSpace(configuration[key]));
}

/// <summary>The capabilities the environment already satisfies, so the run dropped them.</summary>
internal sealed record ProtoSkippedCapabilities(IReadOnlyList<ProtoCapabilityDescriptor> Capabilities);

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
        return builder.ConfigureServices(services =>
        {
            AddCapability(services, capability);
            AddUnconditionalDeclaration(services, capability);
        });
    }

    /// <summary>Records a capability or adapter one application is composed of.</summary>
    public static IProtoApplicationBuilder AddCapability(
        this IProtoApplicationBuilder builder,
        ProtoCapabilityDescriptor capability)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(capability);
        AddCapability(builder.Services, capability);
        AddUnconditionalDeclaration(builder.Services, capability);
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
        AddConditionalCapability(builder.Services, capability, settings ?? []);
        return builder;
    }

    // Descriptors are metadata by value: registering the same one twice - a helper called twice, two
    // integrations declaring the same adapter - must report one capability, while distinct descriptors
    // of the same CLR type still each register.
    private static void AddCapability(IServiceCollection services, ProtoCapabilityDescriptor capability)
        => ProtoRegistration.TryAdd(services, capability, existing => existing == capability);

    // A plain declaration is a promise no environment can withdraw: it is what keeps a descriptor a
    // conditional declaration for the same capability also names.
    private static void AddUnconditionalDeclaration(IServiceCollection services, ProtoCapabilityDescriptor capability)
    {
        var declaration = new ProtoConditionalCapability(capability, []);
        ProtoRegistration.TryAdd(services, declaration, existing => existing == declaration);
    }

    private static void AddConditionalCapability(
        IServiceCollection services,
        ProtoCapabilityDescriptor capability,
        string[] settings)
    {
        AddCapability(services, capability);
        var declaration = new ProtoConditionalCapability(capability, settings.Distinct(StringComparer.Ordinal).ToArray());
        ProtoRegistration.TryAdd(services, declaration, existing => existing == declaration);
    }
}
