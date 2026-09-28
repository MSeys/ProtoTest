namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Composes a built host's run state from the builder's service collection and configuration: the
/// readiness policy, the conditional infrastructure and capability decisions, the run clock and the
/// internal hooks and report sources. <see cref="ProtoHostBuilder.Build"/> constructs the provider and
/// the host; the policy that decides what a build means lives here instead of inline in the builder, so
/// a new conditional concept edits one method in one place.
/// </summary>
internal sealed class ProtoHostComposer(
    IServiceCollection services,
    IConfiguration configuration,
    ProtoTestIdOptions testIdOptions,
    ProtoTraceOptions traceOptions,
    ProtoRunResourceStore runResources,
    ProtoReadinessOptions readinessOptions)
{
    /// <summary>Applies the run composition to the service collection in one pass.</summary>
    public void Compose()
    {
        services.AddSingleton(configuration);
        services.AddSingleton(new ProtoInfrastructureSettings());

        // The run's readiness policy: the single instance every probe, container and readiness consumer
        // shares. It registers through the shared options registrar, so the ProtoTest:Readiness section
        // binds over the code values and validation runs exactly once, where the options resolve - the
        // host forces that resolve when it builds.
        ProtoOptionsRegistration.Configure<ProtoReadinessOptions>(services, () => readinessOptions);

        // Every key any registered infrastructure piece declares counts as provided, including a piece
        // this build will skip because configuration already fills its keys, so a container that will
        // fill a key counts before it starts. The shared environment evaluator reads this set for both
        // the infrastructure-skip and the capability-drop decision. A target's keys count too: its
        // chain promises to fill them, and a chain no provider can serve fails right below.
        var declaredKeys = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoInfrastructureRegistration)
            .SelectMany(descriptor =>
                ((ProtoInfrastructureRegistration)descriptor.ImplementationInstance!).Settings)
            .ToHashSet(StringComparer.Ordinal);
        var chains = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoTargetChain)
            .Select(descriptor => (ProtoTargetChain)descriptor.ImplementationInstance!)
            .ToArray();
        foreach (var chain in chains)
        {
            declaredKeys.UnionWith(chain.Keys);
        }

        // One resolution per target, before the conditional decisions: the first provider whose
        // condition holds serves the target, so only its piece stays in the run store and only its
        // capabilities are declared. A capability a losing provider would have served stays absent.
        var resolutions = ProtoTargetResolver.Resolve(configuration, chains);
        var resolutionsByTarget = new Dictionary<string, ProtoResolvedTarget>(StringComparer.Ordinal);
        if (resolutions.Count > 0)
        {
            services.AddSingleton(new ProtoTargetResolutions(resolutions));
            foreach (var resolution in resolutions)
            {
                foreach (var capability in resolution.Winner.Capabilities)
                {
                    ProtoCapabilityExtensions.AddCapability(services, capability);
                }

                // The winner's own services - client initializers and transports that only exist while
                // it serves the target - register here, after the winner is known and before the
                // provider is built, so a losing provider leaves no service behind.
                resolution.Winner.ConfigureServices(services);
                resolutionsByTarget[resolution.TargetName] = new ProtoResolvedTarget(
                    resolution.TargetName,
                    resolution.Winner.Name,
                    resolution.Winner.Capabilities);
            }
        }

        var skippedInfrastructure = SkipConfiguredInfrastructure(declaredKeys);
        SkipLosingProviders(resolutions, skippedInfrastructure);
        services.AddSingleton(new ProtoSkippedInfrastructure(skippedInfrastructure));

        var skippedCapabilities = DropUnsatisfiedCapabilities(declaredKeys, resolutionsByTarget);
        services.AddSingleton(new ProtoSkippedCapabilities(skippedCapabilities));

        RegisterClock();
        RegisterInternalHooks();
    }

    // Run-owned pieces whose declared keys the environment already configures are not needed:
    // configuration wins, and the piece is not started, owned or released. The decision is fixed here,
    // while configuration is static; AddInfrastructureAlways opts a piece out. A chain-controlled piece
    // is not decided by this rule - its target's resolution below decides whether it is needed.
    private Dictionary<string, string> SkipConfiguredInfrastructure(IReadOnlySet<string> declaredKeys)
    {
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is ProtoInfrastructureRegistration registration
                && registration.IsSatisfiedBy(configuration, declaredKeys))
            {
                skipped[registration.Infrastructure.Id] = "already configured";
                runResources.Remove(registration.Infrastructure.Id);
            }
        }

        return skipped;
    }

    // A provider piece that wins any target it serves starts; one that wins none is not started, owned
    // or released, and the run records the reason its target's record gave. One piece can serve several
    // targets - an AppHost publishes every resource it maps - so the verdict is per piece, not per chain.
    private void SkipLosingProviders(
        IReadOnlyList<ProtoTargetResolution> resolutions,
        Dictionary<string, string> skipped)
    {
        if (resolutions.Count == 0)
        {
            return;
        }

        var winningPieces = resolutions
            .Select(resolution => resolution.Winner.Infrastructure)
            .OfType<IProtoInfrastructure>()
            .Select(piece => piece.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var resolution in resolutions)
        {
            foreach (var skip in resolution.Skipped)
            {
                if (skip.Provider.Infrastructure is not { } piece || winningPieces.Contains(piece.Id))
                {
                    continue;
                }

                skipped[piece.Id] = skip.Reason;
                runResources.Remove(piece.Id);
            }
        }
    }

    // A capability declared with an address condition is dropped when the environment cannot provide
    // that address (WhenProvided) or already provides it elsewhere (UnlessConfigured): HasCapability
    // then answers false and [RequiresCapability] skips, exactly as if the integration that would have
    // served it were never registered. The rule is per declaration, not per descriptor: satisfying one
    // conditional declaration must not drop a capability another, still-unsatisfied declaration and its
    // live integration promise, and a plain declaration stays whatever the environment provides. A
    // chain declaration asks the resolved winner instead of the configuration: it holds while the
    // application is served in-process, and keeps the configured-keys rule when the host has no chain.
    private List<ProtoSkippedCapability> DropUnsatisfiedCapabilities(
        IReadOnlySet<string> declaredKeys,
        IReadOnlyDictionary<string, ProtoResolvedTarget> resolutions)
    {
        var declarations = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoConditionalCapability)
            .Select(descriptor => (ProtoConditionalCapability)descriptor.ImplementationInstance!)
            .Distinct()
            .ToArray();
        var chainDeclarations = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoChainCapability)
            .Select(descriptor => (ProtoChainCapability)descriptor.ImplementationInstance!)
            .Distinct()
            .ToArray();
        var capabilities = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoCapabilityDescriptor)
            .Select(descriptor => (ProtoCapabilityDescriptor)descriptor.ImplementationInstance!)
            .Distinct()
            .ToArray();
        var skipped = new List<ProtoSkippedCapability>();
        foreach (var capability in capabilities)
        {
            var declaredFor = declarations
                .Where(declaration => declaration.Capability == capability)
                .ToArray();
            var chainFor = chainDeclarations
                .Where(declaration => declaration.Capability == capability)
                .ToArray();
            if ((declaredFor.Length == 0 && chainFor.Length == 0)
                || declaredFor.Any(declaration => declaration.IsUnconditional))
            {
                continue;
            }

            if (chainFor.Any(declaration => !declaration.IsDropped(resolutions, configuration, declaredKeys)))
            {
                // The application is served in-process, so the adapter behind this declaration is live.
                continue;
            }

            if (declaredFor.Length > 0
                && !declaredFor.All(declaration => declaration.IsDropped(configuration, declaredKeys)))
            {
                // At least one declaration's integration is still live, so the capability must stay.
                continue;
            }

            for (var index = services.Count - 1; index >= 0; index--)
            {
                if (services[index].ImplementationInstance is ProtoCapabilityDescriptor registered
                    && registered == capability)
                {
                    services.RemoveAt(index);
                }
            }

            var keys = new ProtoKeySet(
                declaredFor.SelectMany(declaration => declaration.Keys)
                    .Concat(chainFor.SelectMany(declaration => declaration.ConfiguredKeys)));
            var reason = string.Join(
                "; ",
                declaredFor.Select(declaration => declaration.DropReason)
                    .Concat(chainFor.Select(declaration => declaration.DropReason(resolutions)))
                    .Distinct(StringComparer.Ordinal));
            skipped.Add(new ProtoSkippedCapability(capability, keys, reason));
        }

        return skipped;
    }

    // The run's clock: the suite's when ConfigureClock registered one, otherwise a clock starting now.
    // Each test seeds its own clock from it, and applications and workers receive the bridge that
    // resolves the active test's clock (or the run's on background flows).
    private void RegisterClock()
    {
        var clock = services
            .Where(descriptor => descriptor.ServiceType == typeof(ProtoClock))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProtoClock>()
            .LastOrDefault() ?? new ProtoClock();
        services.TryAddSingleton(clock);
        // One clock registry per host: a test's clock is found through the host that owns it, so two
        // hosts sharing a test id never overwrite or remove each other's clock.
        services.TryAddSingleton(new ProtoClockRegistry());
        services.TryAddSingleton<TimeProvider>(new ProtoTestTimeProvider(clock));
        services.TryAddSingleton<IProtoTestIdGenerator>(
            _ => new NumericProtoTestIdGenerator(testIdOptions));
        services.TryAddSingleton(traceOptions);
        // The facts the run records about the environment it executed in: captured once here from the
        // trace options, merged into the run's environment attributes by the trace session and projected
        // as report items by the same instance, so wire and report cannot disagree.
        services.TryAddSingleton(ProtoRunMetadata.Capture(traceOptions));
        services.TryAddSingleton(runResources);
        services.TryAddSingleton<ProtoTraceSession>();
        services.TryAddSingleton<IProtoTraceSource>(serviceProvider =>
            serviceProvider.GetRequiredService<ProtoTraceSession>());
    }

    private void RegisterInternalHooks()
    {
        services.AddSingleton<IProtoTestHook, ProtoClientInitializerHook>();
        services.AddSingleton<IProtoTestHook, ProtoClientCompletionHook>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoTraceExportHook>());
        // Registered here, not only by AddSink, so a sink registered directly through DI is exported too;
        // TryAddEnumerable keeps AddSink and the composer from registering the export hook twice.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoSinkExportHook>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoRunGateHook>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoRunResourceHook>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoReportSource, ProtoRunGateReportSource>());
        var findingStore = new ProtoFindingStore();
        services.AddSingleton(findingStore);
        services.AddSingleton<IProtoReportSource>(findingStore);
        var resourceReportStore = new ProtoResourceReportStore();
        services.AddSingleton(resourceReportStore);
        services.AddSingleton<IProtoReportSource>(resourceReportStore);
        services.AddSingleton<IProtoReportSource>(runResources);
        // The run's metadata is a report source itself: no key configured means no item, so a run
        // without CI metadata reports exactly what it did before the option existed.
        services.AddSingleton<IProtoReportSource>(serviceProvider =>
            serviceProvider.GetRequiredService<ProtoRunMetadata>());
    }
}
