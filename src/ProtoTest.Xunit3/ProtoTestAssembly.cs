namespace ProtoTest.Xunit3;

using global::Xunit;
using ProtoTest.Core;

/// <summary>
/// Base assembly fixture for xUnit v3.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
public abstract class ProtoTestAssembly : IAsyncLifetime
{
    private static ProtoHost? _host;

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before host initialization.</exception>
    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Register your fixture with [assembly: AssemblyFixture(...)].");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var builder = new ProtoHostBuilder();
        Configure(builder);

        _host = builder.Build();

        await _host.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                await _host.DisposeAsync();
                _host = null;
            }
        }
    }

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    /// <param name="builder">The host builder instance.</param>
    protected abstract void Configure(IProtoHostBuilder builder);
}