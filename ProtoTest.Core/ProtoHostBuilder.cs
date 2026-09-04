namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Implements the builder pattern for configuring and constructing a <see cref="ProtoHost"/> instance.
/// </summary>
public sealed class ProtoHostBuilder : IProtoHostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();

    /// <inheritdoc />
    public IProtoHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
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
        var rootProvider = _services.BuildServiceProvider();
        var hooks = rootProvider.GetServices<IProtoHook>();

        return new ProtoHost(rootProvider, hooks);
    }
}