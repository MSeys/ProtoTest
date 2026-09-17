namespace ProtoTest.TUnit;

using ProtoTest.Core;

/// <summary>
/// Abstract base class for TUnit assembly configuration.
/// Manages the root <see cref="ProtoHost"/> lifecycle for the assembly.
/// </summary>
public abstract class ProtoTestAssembly
{
    private static readonly ProtoTestHostLifetime Lifetime = new(
        "Ensure your setup class inherits from ProtoTestAssembly.");

    /// <summary>
    /// Gets the initialized global <see cref="ProtoHost"/> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before host initialization.</exception>
    public static ProtoHost Host => Lifetime.Host;

    /// <summary>
    /// Builds and starts the global <see cref="ProtoHost"/> instance asynchronously.
    /// </summary>
    /// <param name="configure">Delegate to configure the <see cref="IProtoHostBuilder"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown if the host has already been initialized.</exception>
    protected static Task InitializeAsync(Action<IProtoHostBuilder> configure)
        => Lifetime.StartAsync(configure);

    /// <summary>
    /// Stops and disposes the global <see cref="ProtoHost"/> instance asynchronously.
    /// </summary>
    protected static Task CleanupAsync() => Lifetime.StopAsync();
}
