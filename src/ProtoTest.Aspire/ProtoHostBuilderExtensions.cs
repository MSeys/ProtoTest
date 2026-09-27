namespace ProtoTest.Aspire;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Aspire.Internal;
using ProtoTest.Core;

/// <summary>Registers the Aspire AppHost the run starts, owns and stops, and the providers that serve targets through it.</summary>
public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Hosts an Aspire AppHost for the whole run: the suite runs the AppHost's own entry point
    /// through the Aspire testing host, starts it after the infrastructure registered before it, and
    /// stops it when the run ends. Each <paramref name="resource"/> becomes an application target:
    /// its endpoint is published as <c>ProtoTest:Applications:{resource}:BaseUrl</c>, so the
    /// application's clients and the readiness probe resolve that one address.
    /// </summary>
    /// <remarks>
    /// Register the AppHost before the targets whose providers reference it with
    /// <c>UseAspireResource</c>; those providers serve when <c>ProtoTest:Aspire:Enabled</c> is set, and
    /// a configured provider earlier in their chain wins over the AppHost.
    /// </remarks>
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
        var composition = Compose(builder);
        if (composition.Pieces.TryGetValue(typeof(TEntryPoint), out var existing))
        {
            var registered = (ProtoAspireAppHost<TEntryPoint>)existing;
            if (registered.SameCompositionAs(candidate))
            {
                return builder;
            }

            throw new InvalidOperationException(
                $"An Aspire AppHost for {typeof(TEntryPoint).FullName} already hosts {registered.Describe()}; " +
                $"register {candidate.Describe()} under a different entry point instead.");
        }

        // A conflicting registration must fail before anything is applied: check the resources
        // the registry already owns, like the same-name check above.
        foreach (var published in resources)
        {
            var owner = composition.Registry.OwnerOf(published);
            if (owner is not null && owner != typeof(TEntryPoint))
            {
                throw new InvalidOperationException(
                    $"Aspire resource '{published}' is already hosted by {owner.FullName}; " +
                    $"register {typeof(TEntryPoint).FullName} under a different resource name instead.");
            }
        }

        // The plain AppHost registration keeps its all-configured skip rule until a suite adopts a
        // chain; still the only way to host the AppHost the chain providers reference.
#pragma warning disable CS0618
        builder.AddInfrastructure(candidate, candidate.PublishKeys.ToArray());
#pragma warning restore CS0618
        composition.RegisterRegistry(builder);
        foreach (var published in resources)
        {
            composition.Registry.Add(typeof(TEntryPoint), published, candidate.KeyFor(published));
        }

        composition.Pieces.Add(typeof(TEntryPoint), candidate);
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            typeof(TEntryPoint).Assembly.GetName().Name ?? typeof(TEntryPoint).FullName!,
            ProtoCapabilityKinds.Aspire,
            "ProtoTest.Aspire"));
    }

    /// <summary>
    /// Adds an AppHost provider to the application's chain: the mapped resource's endpoint fills the
    /// application's derived <c>ProtoTest:Applications:{name}:BaseUrl</c> key when the provider wins.
    /// The provider serves when <c>ProtoTest:Aspire:Enabled</c> is set; put a configured provider
    /// first and the in-process fallback last, so the same composition runs in every mode.
    /// </summary>
    /// <param name="application">The application target.</param>
    /// <param name="resource">The AppHost resource whose endpoint serves the application.</param>
    public static IProtoApplicationBuilder UseAspireResource<TEntryPoint>(
        this IProtoApplicationBuilder application,
        string resource)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var composition = ProtoAspireComposition.For(application.Services);
        var piece = composition.PieceFor<TEntryPoint>(resource);
        var key = $"{ProtoApplication.SectionPath}:{application.ApplicationName}:BaseUrl";
        piece.AddPublish(resource, key, ProtoAspirePublishKind.Endpoint);
        application.Providers.Use(AspireProvider<TEntryPoint>(resource, piece));
        composition.RegisterRegistry(application.Host);
        composition.Registry.Add(typeof(TEntryPoint), resource, key);
        return application;
    }

    /// <summary>
    /// Adds an AppHost provider to a target's chain: the mapped resource's connection string fills
    /// every key the target declares when the provider wins, so the run's test-side readers resolve
    /// the AppHost's database or broker. The provider serves when <c>ProtoTest:Aspire:Enabled</c> is
    /// set; a configured provider earlier in the chain wins over it.
    /// </summary>
    /// <param name="chain">The target's provider chain.</param>
    /// <param name="resource">The AppHost resource whose connection string fills the target's keys.</param>
    public static IProtoProviderChainBuilder UseAspireResource<TEntryPoint>(
        this IProtoProviderChainBuilder chain,
        string resource)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var composition = ProtoAspireComposition.For(chain.Services);
        var piece = composition.PieceFor<TEntryPoint>(resource);
        foreach (var key in chain.Keys)
        {
            piece.AddPublish(resource, key, ProtoAspirePublishKind.ConnectionString);
        }

        if (chain.Keys.Count > 0)
        {
            composition.Registry.Add(typeof(TEntryPoint), resource, chain.Keys[0]);
        }

        return chain.Use(AspireProvider<TEntryPoint>(resource, piece));
    }

    /// <summary>
    /// Records that the AppHost registered on this builder publishes <paramref name="resource"/>'s
    /// connection string under <paramref name="key"/>, so the run's readers resolve the AppHost's
    /// database or broker under a key no target declares. Register the AppHost first; a configured key
    /// is never overwritten, and the AppHost only starts when its providers win a target.
    /// </summary>
    /// <param name="builder">The host builder with a registered AppHost.</param>
    /// <param name="resource">The AppHost resource, for example <c>postgres</c>.</param>
    /// <param name="key">The key the resource's connection string fills.</param>
    public static IProtoHostBuilder MapConnectionString(this IProtoHostBuilder builder, string resource, string key)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var composition = Compose(builder);
        var owner = composition.Registry.OwnerOf(resource)
            ?? throw new InvalidOperationException(
                $"Aspire resource '{resource}' is not hosted by an AppHost on this builder; register one with " +
                $"AddAspireAppHost<TEntryPoint>(\"{resource}\") first.");
        if (!composition.Pieces.TryGetValue(owner, out var piece))
        {
            throw new InvalidOperationException(
                $"Aspire resource '{resource}' is registered without an AppHost piece; register it with AddAspireAppHost<TEntryPoint>().");
        }

        piece.AddPublish(resource, key, ProtoAspirePublishKind.ConnectionString, replaceEndpoints: true);
        composition.Registry.Add(owner, resource, key);
        // The mapped key joins the AppHost piece's plain registration, so its all-configured skip
        // rule and the provided-key decisions see it; UseAspireResource is the chain replacement.
#pragma warning disable CS0618
        builder.AddInfrastructure(piece, key);
#pragma warning restore CS0618
        return builder;
    }

    // The composition lives in the builder's service collection, so a host-level call and a provider
    // reached from inside AddApplication resolve the same AppHost pieces.
    private static ProtoAspireComposition Compose(IProtoHostBuilder builder)
    {
        ProtoAspireComposition? composition = null;
        builder.ConfigureServices(services => composition = ProtoAspireComposition.For(services));
        return composition
            ?? throw new InvalidOperationException(
                "The host builder did not expose its service collection while registering the Aspire AppHost.");
    }

    private static ProtoTargetProvider AspireProvider<TEntryPoint>(string resource, IProtoAspireAppHost piece)
        where TEntryPoint : class
        => new($"aspire:{resource}", piece, ProtoProviderConditions.Selected(ProtoAspireOptions.SelectionKey))
        {
            Capabilities =
            [
                new ProtoCapabilityDescriptor(
                    typeof(TEntryPoint).Assembly.GetName().Name ?? typeof(TEntryPoint).FullName!,
                    ProtoCapabilityKinds.Aspire,
                    "ProtoTest.Aspire")
            ]
        };
}
