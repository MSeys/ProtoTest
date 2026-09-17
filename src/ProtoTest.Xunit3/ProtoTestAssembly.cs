namespace ProtoTest.Xunit3;

using Xunit;
using ProtoTest.Core;

/// <summary>
/// Base assembly fixture for xUnit v3.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
public abstract class ProtoTestAssembly : IAsyncLifetime
{
    private static readonly ProtoTestHostLifetime Lifetime = new(
        "Register your fixture with [assembly: AssemblyFixture(...)].");

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before host initialization.</exception>
    public static ProtoHost Host => Lifetime.Host;

    /// <inheritdoc />
    public ValueTask InitializeAsync() => new(Lifetime.StartAsync(Configure));

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(Lifetime.StopAsync());

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    /// <param name="builder">The host builder instance.</param>
    protected abstract void Configure(IProtoHostBuilder builder);
}
