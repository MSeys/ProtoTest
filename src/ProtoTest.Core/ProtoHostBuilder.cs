namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Implements the builder pattern for configuring and constructing a <see cref="ProtoHost"/> instance.
/// </summary>
public sealed class ProtoHostBuilder : IProtoHostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly ConfigurationBuilder _configurationBuilder = new();
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

        // Internal hooks
        _services.AddSingleton<IProtoTestHook, ProtoClientInitializerHook>();

        var rootProvider = _services.BuildServiceProvider();
        return new ProtoHost(rootProvider);
    }
}