namespace ProtoTest.Core;

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
    public const string Protocol = "protocol";
    public const string Browser = "browser";
    public const string Store = "store";
    public const string Broker = "broker";
    public const string Data = "data";
    public const string Document = "document";
}

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
        AddCapability(builder.Services, capability);
        return builder;
    }

    // Descriptors are metadata by value: registering the same one twice - a helper called twice, two
    // integrations declaring the same adapter - must report one capability, while distinct descriptors
    // of the same CLR type still each register.
    private static void AddCapability(IServiceCollection services, ProtoCapabilityDescriptor capability)
    {
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)
                && Equals(descriptor.ImplementationInstance, capability)))
        {
            return;
        }

        services.AddSingleton(capability);
    }
}
