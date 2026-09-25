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

    /// <inheritdoc />
    public IProtoHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
        return this;
    }
    /// <inheritdoc />
    public IProtoHostBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_configurationBuilder);
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder ConfigureTestIds(Action<ProtoTestIdOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
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
        _services.AddSingleton<IProtoTestHook, THook>();
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddRunHook<TRunHook>() where TRunHook : class, IProtoRunHook
    {
        _services.AddSingleton<IProtoRunHook, TRunHook>();
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddRunGate<TGate>() where TGate : class, IProtoRunGate
    {
        _services.AddSingleton<IProtoRunGate, TGate>();
        return this;
    }

    /// <inheritdoc />
    public IProtoHostBuilder AddRunGate(string name, Func<ProtoRunGateContext, ProtoRunGateResult> evaluate)
    {
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
            throw new InvalidOperationException(
                "The ProtoHostBuilder has already built a ProtoHost; configure a new builder instead.");
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

        // The run's clock: the suite's when ConfigureClock registered one, otherwise a clock starting now.
        // Each test seeds its own clock from it, and applications and workers receive the bridge that
        // resolves the active test's clock (or the run's on background flows).
        var clock = _services
            .Where(descriptor => descriptor.ServiceType == typeof(ProtoClock))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProtoClock>()
            .LastOrDefault() ?? new ProtoClock();
        _services.TryAddSingleton(clock);
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
