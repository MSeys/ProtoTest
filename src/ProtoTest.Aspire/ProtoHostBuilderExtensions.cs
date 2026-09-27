namespace ProtoTest.Aspire;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Aspire.Internal;
using ProtoTest.Core;

/// <summary>Registers the Aspire AppHost the run starts, owns and stops.</summary>
public static class ProtoHostBuilderExtensions
{
    private static readonly ConditionalWeakTable<IProtoHostBuilder, Registrations> RegisteredHosts = new();

    /// <summary>
    /// Hosts an Aspire AppHost for the whole run: the suite runs the AppHost's own entry point
    /// through the Aspire testing host, starts it after the infrastructure registered before it, and
    /// stops it when the run ends. Each <paramref name="resource"/> becomes an application target:
    /// its endpoint is published as <c>ProtoTest:Applications:{resource}:BaseUrl</c>, so the
    /// application's clients and the readiness probe resolve that one address.
    /// </summary>
    /// <typeparam name="TEntryPoint">A public type in the AppHost assembly.</typeparam>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="resource">The first Aspire resource to publish; at least one is required.</param>
    /// <param name="moreResources">Further resources to publish under their own application names.</param>
    public static IProtoHostBuilder AddAspireAppHost<TEntryPoint>(
        this IProtoHostBuilder builder,
        string resource,
        params string[] moreResources)
        where TEntryPoint : class
        => AddAspireAppHost<TEntryPoint>(builder, configure: null, resource, moreResources);

    /// <summary>
    /// Hosts an Aspire AppHost for the whole run, with options: rename an application target, read a
    /// different endpoint of a resource, or pass settings to the AppHost.
    /// </summary>
    /// <typeparam name="TEntryPoint">A public type in the AppHost assembly.</typeparam>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="configure">The AppHost choices; <see langword="null"/> publishes every resource under its own name.</param>
    /// <param name="resource">The first Aspire resource to publish; at least one is required.</param>
    /// <param name="moreResources">Further resources to publish under their own application names.</param>
    public static IProtoHostBuilder AddAspireAppHost<TEntryPoint>(
        this IProtoHostBuilder builder,
        Action<ProtoAspireOptions>? configure,
        string resource,
        params string[] moreResources)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var resources = new[] { resource }.Concat(moreResources ?? []).ToArray();
        var candidate = new ProtoAspireAppHost<TEntryPoint>(resources, configure);

        var registrations = RegisteredHosts.GetValue(builder, static _ => new Registrations());
        if (registrations.Hosts.TryGetValue(typeof(TEntryPoint), out var registered))
        {
            if (registered.IsSame(candidate))
            {
                return builder;
            }

            throw new InvalidOperationException(
                $"An Aspire AppHost for {typeof(TEntryPoint).FullName} already hosts {registered.Description}; " +
                $"register {candidate.Describe()} under a different entry point instead.");
        }

        // A conflicting registration must fail before anything is applied: check the resources
        // the registry already owns, like the same-name check above.
        foreach (var published in resources)
        {
            var owner = registrations.Registry.OwnerOf(published);
            if (owner is not null && owner != typeof(TEntryPoint))
            {
                throw new InvalidOperationException(
                    $"Aspire resource '{published}' is already hosted by {owner.FullName}; " +
                    $"register {typeof(TEntryPoint).FullName} under a different resource name instead.");
            }
        }

        builder.AddInfrastructure(candidate, candidate.BaseUrlKeys.ToArray());
        if (!registrations.RegistryRegistered)
        {
            builder.ConfigureServices(services => services.AddSingleton(registrations.Registry));
            registrations.RegistryRegistered = true;
        }

        foreach (var published in resources)
        {
            registrations.Registry.Add(typeof(TEntryPoint), published, candidate.ApplicationFor(published));
        }

        registrations.Hosts.Add(
            typeof(TEntryPoint),
            new Registration(
                other => other is ProtoAspireAppHost<TEntryPoint> typed && candidate.SameCompositionAs(typed),
                candidate.Describe()));
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            typeof(TEntryPoint).Assembly.GetName().Name ?? typeof(TEntryPoint).FullName!,
            ProtoCapabilityKinds.Aspire,
            "ProtoTest.Aspire"));
    }

    private sealed class Registrations
    {
        public Dictionary<Type, Registration> Hosts { get; } = new();

        public ProtoAspireRegistry Registry { get; } = new();

        public bool RegistryRegistered { get; set; }
    }

    private sealed class Registration(Func<object, bool> sameComposition, string description)
    {
        public string Description { get; } = description;

        public bool IsSame(object candidate) => sameComposition(candidate);
    }
}
