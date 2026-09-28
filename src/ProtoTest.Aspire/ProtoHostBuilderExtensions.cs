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
    /// The AppHost serves only when selected: <c>ProtoTest:Aspire:Enabled</c> selects every
    /// resource, a resource's own key from <see cref="ProtoAspireOptions.ResourceSelectionKey"/>
    /// selects that resource, and neither means the AppHost never starts and every target resolves
    /// elsewhere. A key configuration already fills is never masked: every key configured steps the
    /// AppHost aside entirely, and with only some configured it starts for the rest and publishes
    /// only the selected keys configuration does not fill. Register the AppHost before the targets
    /// whose providers reference it with <c>UseAspireResource</c>. The AppHost receives the run's
    /// configuration and the settings earlier infrastructure published as command-line arguments, so
    /// its own graph can read the addresses the suite resolved.
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

        // The AppHost is one target: a configured environment steps it aside without starting it,
        // the selection keys decide whether it serves, and the run keeps going on the other
        // providers when neither holds. The step-aside reads the piece's own mappings, so a
        // MapConnectionString mapping added after this call still counts.
        builder.AddInfrastructure(
            candidate.Id,
            chain => chain
                .Use(new ProtoTargetProvider("configured", Condition: new ProtoAspireConfiguredCondition(candidate)))
                .Use(AppHostProvider<TEntryPoint>(candidate, resources))
                .Use(new ProtoTargetProvider("unselected")),
            candidate.PublishKeys.ToArray());
        composition.RegisterRegistry(builder);
        foreach (var published in resources)
        {
            composition.Registry.Add(typeof(TEntryPoint), published, candidate.KeyFor(published));
        }

        composition.Pieces.Add(typeof(TEntryPoint), candidate);
        return builder;
    }

    /// <summary>
    /// Adds an AppHost provider to the application's chain: the mapped resource's endpoint fills the
    /// application's derived <c>ProtoTest:Applications:{name}:BaseUrl</c> key when the provider wins.
    /// The provider serves when <c>ProtoTest:Aspire:Enabled</c> or the resource's own key from
    /// <see cref="ProtoAspireOptions.ResourceSelectionKey"/> is set; put a configured provider first
    /// and the in-process fallback last, so the same composition runs in every mode.
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
    /// the AppHost's database or broker. The provider serves when <c>ProtoTest:Aspire:Enabled</c> or
    /// the resource's own key from <see cref="ProtoAspireOptions.ResourceSelectionKey"/> is set; a
    /// configured provider earlier in the chain wins over it.
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
        => new(
            $"aspire:{resource}",
            piece,
            ProtoProviderConditions.Selected(
                ProtoAspireOptions.SelectionKey,
                ProtoAspireOptions.ResourceSelectionKey(resource)))
        {
            Capabilities = [AspireCapability<TEntryPoint>()]
        };

    /// <summary>
    /// The provider that serves an application-or-resource target through the AppHost: it holds when
    /// the global selection key or any of the AppHost's resources' own keys is set, and starts the
    /// piece once when it wins any target.
    /// </summary>
    private static ProtoTargetProvider AppHostProvider<TEntryPoint>(
        IProtoAspireAppHost piece,
        IReadOnlyList<string> resources)
        where TEntryPoint : class
        => new(
            $"aspire:{typeof(TEntryPoint).Assembly.GetName().Name}",
            piece,
            ProtoProviderConditions.Selected(
                ProtoAspireOptions.SelectionKey,
                [.. resources.Select(ProtoAspireOptions.ResourceSelectionKey)]))
        {
            Capabilities = [AspireCapability<TEntryPoint>()]
        };

    private static ProtoCapabilityDescriptor AspireCapability<TEntryPoint>()
        where TEntryPoint : class
        => new(
            typeof(TEntryPoint).Assembly.GetName().Name ?? typeof(TEntryPoint).FullName!,
            ProtoCapabilityKinds.Aspire,
            "ProtoTest.Aspire");
}
