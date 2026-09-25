namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core.Internal;

/// <summary>
/// Implements the builder pattern for configuring and constructing a <see cref="ProtoHost"/> instance.
/// </summary>
public sealed class ProtoHostBuilder : IProtoHostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly ConfigurationBuilder _configurationBuilder = new();
    private readonly ProtoTestIdOptions _testIdOptions = new();
    private readonly ProtoTraceOptions _traceOptions = new();
    private readonly ProtoRunResourceStore _runResources = new();
    private bool _built;

    /// <summary>The one message every entry that is called after <see cref="Build"/> reports.</summary>
    internal const string BuiltMessage =
        "The ProtoHostBuilder has already built a ProtoHost; configure a new builder instead.";

    /// <summary>Whether a host was already built; the application builder shares this terminal rule.</summary>
    internal bool IsBuilt => _built;

    /// <inheritdoc />
    public IProtoHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ThrowIfBuilt();
        configure(_services);
        return this;
    }
    /// <inheritdoc />
    public IProtoHostBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ThrowIfBuilt();
        configure(_configurationBuilder);
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder ConfigureTestIds(Action<ProtoTestIdOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ThrowIfBuilt();
        configure(_testIdOptions);
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder ConfigureTracing(Action<ProtoTraceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ThrowIfBuilt();
        configure(_traceOptions);
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddTestHook<THook>() where THook : class, IProtoTestHook
    {
        ThrowIfBuilt();
        _services.AddSingleton<IProtoTestHook, THook>();
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddRunHook<TRunHook>() where TRunHook : class, IProtoRunHook
    {
        ThrowIfBuilt();
        _services.AddSingleton<IProtoRunHook, TRunHook>();
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddRunGate<TGate>() where TGate : class, IProtoRunGate
    {
        ThrowIfBuilt();
        _services.AddSingleton<IProtoRunGate, TGate>();
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddRunGate(string name, Func<ProtoRunGateContext, ProtoRunGateResult> evaluate)
    {
        ThrowIfBuilt();
        _services.AddSingleton<IProtoRunGate>(new ProtoRunGate(name, evaluate));
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddResource(IProtoResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ThrowIfBuilt();
        _runResources.Add(resource);
        return this;
    }

    /// <summary>
    /// Registering after the host was built would mutate live host state (a running run's resources or
    /// options) instead of composing the host, so it is rejected like a second build.
    /// </summary>
    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(BuiltMessage);
        }
    }

    /// <inheritdoc />
    public ProtoHost Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("A ProtoHostBuilder can only build one ProtoHost.");
        }

        _built = true;

        // Build and register IConfiguration
        IConfiguration configuration = _configurationBuilder.Build();
        _services.AddSingleton(configuration);
        _services.AddSingleton(new ProtoInfrastructureSettings());

        // The run's readiness policy: the instance ConfigureReadiness/probes share, with the
        // ProtoTest:Readiness section bound over the code values, so one owner governs host probes and
        // the containers the run starts.
        var readinessOptions = ProtoReadinessExtensions.ResolveOptions(this);
        readinessOptions.BindFromConfiguration(configuration);
        readinessOptions.Validate();
        _services.TryAddSingleton(readinessOptions);

        // Run-owned pieces whose declared keys the environment already configures are not needed:
        // configuration wins, and the piece is not started, owned or released. The decision is fixed
        // here, while configuration is static; AddInfrastructureAlways opts a piece out.
        var skippedInfrastructure = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in _services)
        {
            if (descriptor.ImplementationInstance is ProtoInfrastructureRegistration registration
                && registration.IsSatisfiedBy(configuration))
            {
                skippedInfrastructure.Add(registration.Infrastructure.Id);
                _runResources.Remove(registration.Infrastructure.Id);
            }
        }

        _services.AddSingleton(new ProtoSkippedInfrastructure(skippedInfrastructure));

        // A capability declared with an address condition is dropped when the environment cannot
        // provide that address (WhenProvided) or already provides it elsewhere (UnlessConfigured):
        // HasCapability then answers false and [RequiresCapability] skips, exactly as if the
        // integration that would have served it were never registered. The rule is per declaration,
        // not per descriptor: satisfying one conditional declaration must not drop a capability
        // another, still-unsatisfied declaration and its live integration promise, and a plain
        // declaration stays whatever the environment provides.
        //
        // A key counts as provided when configuration has a value for it or a registered
        // infrastructure piece declares it, including a piece this build skips because configuration
        // already fills its keys, so a container that will fill a key counts before it starts.
        var declaredKeys = _services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoInfrastructureRegistration)
            .SelectMany(descriptor =>
                ((ProtoInfrastructureRegistration)descriptor.ImplementationInstance!).Settings)
            .ToHashSet(StringComparer.Ordinal);
        var declarations = _services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoConditionalCapability)
            .Select(descriptor => (ProtoConditionalCapability)descriptor.ImplementationInstance!)
            .Distinct()
            .ToArray();
        var capabilities = _services
            .Where(descriptor => descriptor.ImplementationInstance is ProtoCapabilityDescriptor)
            .Select(descriptor => (ProtoCapabilityDescriptor)descriptor.ImplementationInstance!)
            .Distinct()
            .ToArray();
        var skippedCapabilities = new List<ProtoSkippedCapability>();
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

            for (var index = _services.Count - 1; index >= 0; index--)
            {
                if (_services[index].ImplementationInstance is ProtoCapabilityDescriptor registered
                    && registered == capability)
                {
                    _services.RemoveAt(index);
                }
            }

            var keys = new ProtoKeySet(declaredFor.SelectMany(declaration => declaration.Keys));
            var reason = string.Join(
                "; ",
                declaredFor.Select(declaration => declaration.DropReason).Distinct(StringComparer.Ordinal));
            skippedCapabilities.Add(new ProtoSkippedCapability(capability, keys, reason));
        }

        _services.AddSingleton(new ProtoSkippedCapabilities(skippedCapabilities));

        // The run's clock: the suite's when ConfigureClock registered one, otherwise a clock starting now.
        // Each test seeds its own clock from it, and applications and workers receive the bridge that
        // resolves the active test's clock (or the run's on background flows).
        var clock = _services
            .Where(descriptor => descriptor.ServiceType == typeof(ProtoClock))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProtoClock>()
            .LastOrDefault() ?? new ProtoClock();
        _services.TryAddSingleton(clock);
        // One clock registry per host: a test's clock is found through the host that owns it, so two
        // hosts sharing a test id never overwrite or remove each other's clock.
        _services.TryAddSingleton(new ProtoClockRegistry());
        _services.TryAddSingleton<TimeProvider>(new ProtoTestTimeProvider(clock));
        _services.TryAddSingleton<IProtoTestIdGenerator>(
            _ => new NumericProtoTestIdGenerator(_testIdOptions));
        _services.TryAddSingleton(_traceOptions);
        _services.TryAddSingleton(_runResources);
        _services.TryAddSingleton<ProtoTraceSession>();
        _services.TryAddSingleton<IProtoTraceSource>(services => services.GetRequiredService<ProtoTraceSession>());

        // Internal hooks
        _services.AddSingleton<IProtoTestHook, ProtoClientInitializerHook>();
        _services.AddSingleton<IProtoTestHook, ProtoClientCompletionHook>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoTraceExportHook>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoRunGateHook>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoRunResourceHook>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoReportSource, ProtoRunGateReportSource>());
        var findingStore = new ProtoFindingStore();
        _services.AddSingleton(findingStore);
        _services.AddSingleton<IProtoReportSource>(findingStore);
        var resourceReportStore = new ProtoResourceReportStore();
        _services.AddSingleton(resourceReportStore);
        _services.AddSingleton<IProtoReportSource>(resourceReportStore);
        _services.AddSingleton<IProtoReportSource>(_runResources);

        var rootProvider = _services.BuildServiceProvider();
        try
        {
            // Collectors are singletons: constructing them here turns a bad specification or schema -
            // an OpenAPI document or GraphQL SDL that cannot be read - into a build failure instead of
            // one that surfaces on the first test that records an observation.
            _ = rootProvider.GetServices<IProtoCollector>().ToArray();
            return new ProtoHost(rootProvider);
        }
        catch
        {
            try
            {
                rootProvider.Dispose();
            }
            catch (Exception)
            {
                // Disposing a provider that holds async-only services can fail; the original
                // construction failure is what the caller needs to see.
            }

            throw;
        }
    }
}
