namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Defines the builder contract for constructing and configuring a <see cref="ProtoHost"/>.
/// </summary>
public interface IProtoHostBuilder
{
    /// <summary>
    /// Registers dependencies with the internal service collection.
    /// </summary>
    /// <param name="configure">A delegate to configure the <see cref="IServiceCollection"/>.</param>
    /// <returns>The current <see cref="IProtoHostBuilder"/> instance.</returns>
    IProtoHostBuilder ConfigureServices(Action<IServiceCollection> configure);

    /// <summary>
    /// Registers a global execution lifecycle hook.
    /// </summary>
    /// <typeparam name="THook">The type of the hook to register.</typeparam>
    /// <returns>The current <see cref="IProtoHostBuilder"/> instance.</returns>
    IProtoHostBuilder AddHook<THook>() where THook : class, IProtoHook;

    /// <summary>
    /// Builds and initializes the configured <see cref="ProtoHost"/> instance.
    /// </summary>
    /// <returns>A fully configured <see cref="ProtoHost"/>.</returns>
    ProtoHost Build();
}