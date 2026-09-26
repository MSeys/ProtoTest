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
        // the infrastructure-skip and the capability-drop decision.
        var declaredKeys = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoInfrastructureRegistration)
            .SelectMany(descriptor =>
                ((ProtoInfrastructureRegistration)descriptor.ImplementationInstance!).Settings)
            .ToHashSet(StringComparer.Ordinal);

        var skippedInfrastructure = SkipConfiguredInfrastructure(declaredKeys);
        services.AddSingleton(new ProtoSkippedInfrastructure(skippedInfrastructure));

        var skippedCapabilities = DropUnsatisfiedCapabilities(declaredKeys);
        services.AddSingleton(new ProtoSkippedCapabilities(skippedCapabilities));

        RegisterClock();
        RegisterInternalHooks();
    }

    // Run-owned pieces whose declared keys the environment already configures are not needed:
    // configuration wins, and the piece is not started, owned or released. The decision is fixed here,
    // while configuration is static; AddInfrastructureAlways opts a piece out.
    private HashSet<string> SkipConfiguredInfrastructure(IReadOnlySet<string> declaredKeys)
    {
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is ProtoInfrastructureRegistration registration
                && registration.IsSatisfiedBy(configuration, declaredKeys))
            {
                skipped.Add(registration.Infrastructure.Id);
                runResources.Remove(registration.Infrastructure.Id);
            }
        }

        return skipped;
    }

    // A capability declared with an address condition is dropped when the environment cannot provide
    // that address (WhenProvided) or already provides it elsewhere (UnlessConfigured): HasCapability
    // then answers false and [RequiresCapability] skips, exactly as if the integration that would have
    // served it were never registered. The rule is per declaration, not per descriptor: satisfying one
    // conditional declaration must not drop a capability another, still-unsatisfied declaration and its
    // live integration promise, and a plain declaration stays whatever the environment provides.
    private List<ProtoSkippedCapability> DropUnsatisfiedCapabilities(IReadOnlySet<string> declaredKeys)
    {
        var declarations = services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoConditionalCapability)
            .Select(descriptor => (ProtoConditionalCapability)descriptor.ImplementationInstance!)
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
            if (declaredFor.Length == 0 || declaredFor.Any(declaration => declaration.IsUnconditional))
            {
                continue;
            }

            if (!declaredFor.All(declaration => declaration.IsDropped(configuration, declaredKeys)))
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

            var keys = new ProtoKeySet(declaredFor.SelectMany(declaration => declaration.Keys));
            var reason = string.Join(
                "; ",
                declaredFor.Select(declaration => declaration.DropReason).Distinct(StringComparer.Ordinal));
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
