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
    public IProtoHostBuilder AddHook<THook>() where THook : class, IProtoHook
    {
        _services.AddSingleton<IProtoHook, THook>();
        return this;
    }

    /// <inheritdoc />
    public ProtoHost Build()
    {
        // Build and register IConfiguration
        IConfiguration configuration = _configurationBuilder.Build();
        _services.AddSingleton(configuration);

        // Internal hooks
        _services.AddSingleton<IProtoHook, ProtoClientInitializerHook>();

        var rootProvider = _services.BuildServiceProvider();
        var hooks = rootProvider.GetServices<IProtoHook>();

        return new ProtoHost(rootProvider, hooks);
    }
}