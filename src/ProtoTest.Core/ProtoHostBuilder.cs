namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Implements the builder pattern for configuring and constructing a <see cref="ProtoHost"/> instance.
/// </summary>
public sealed class ProtoHostBuilder : IProtoHostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly ConfigurationBuilder _configurationBuilder = new();
    private readonly ProtoTestIdOptions _testIdOptions = new();
    private readonly ProtoTraceOptions _traceOptions = new();
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
        _services.TryAddSingleton<IProtoTestIdGenerator>(
            _ => new NumericProtoTestIdGenerator(_testIdOptions));
        _services.TryAddSingleton(_traceOptions);
        _services.TryAddSingleton<ProtoTraceSession>();
        _services.TryAddSingleton<IProtoTraceSource>(services => services.GetRequiredService<ProtoTraceSession>());

        // Internal hooks
        _services.AddSingleton<IProtoTestHook, ProtoClientInitializerHook>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoRunHook, ProtoTraceExportHook>());

        var rootProvider = _services.BuildServiceProvider();
        try
        {
            return new ProtoHost(rootProvider);
        }
        catch
        {
            rootProvider.Dispose();
            throw;
        }
    }
}
