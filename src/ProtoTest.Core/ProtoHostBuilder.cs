namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// Implements the builder pattern for configuring and constructing a <see cref="ProtoHost"/> instance.
/// </summary>
public sealed class ProtoHostBuilder : IProtoHostBuilder, IProtoComposableBuilder
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

    /// <summary>
    /// Lets builder extensions that keep their own state - the readiness policy, for example - apply the
    /// same terminal rule as the builder's own entries.
    /// </summary>
    void IProtoComposableBuilder.ThrowIfBuilt() => ThrowIfBuilt();

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
            throw new InvalidOperationException(BuiltMessage);
        }

        _built = true;

        IConfiguration configuration = _configurationBuilder.Build();
        // One composer owns what a build means: the conditional decisions, the run clock and the internal
        // hooks. It mutates the collection in place, so the provider built below is complete.
        var composer = new ProtoHostComposer(
            _services,
            configuration,
            _testIdOptions,
            _traceOptions,
            _runResources,
            ProtoReadinessExtensions.ResolveOptions(this),
            ProtoRedactionExtensions.ResolveOptions(this));
        composer.Compose();

        var rootProvider = _services.BuildServiceProvider();
        try
        {
            // Collectors are singletons: constructing them here turns a bad specification or schema -
            // an OpenAPI document or GraphQL SDL that cannot be read - into a build failure instead of
            // one that surfaces on the first test that records an observation. Resolving the readiness
            // options runs their one binding and validation, so a bad ProtoTest:Readiness fails the
            // build rather than the first probe.
            _ = rootProvider.GetServices<IProtoCollector>().ToArray();
            _ = rootProvider.GetRequiredService<ProtoReadinessOptions>();
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
