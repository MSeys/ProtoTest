namespace ProtoTest.Xunit;

using global::Xunit;
using ProtoTest.Core;

/// <summary>
/// Base fixture for global assembly setup in xUnit v2.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
public abstract class ProtoTestAssembly : IAsyncLifetime
{
    private static readonly ProtoTestHostLifetime Lifetime = new(
        "Ensure your collection fixture inherits from ProtoTestAssembly.");

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before host initialization.</exception>
    public static ProtoHost Host => Lifetime.Host;

    /// <inheritdoc />
    public Task InitializeAsync() => Lifetime.StartAsync(Configure);

    /// <inheritdoc />
    public Task DisposeAsync() => Lifetime.StopAsync();

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    /// <param name="builder">The host builder instance.</param>
    protected abstract void Configure(IProtoHostBuilder builder);
}
