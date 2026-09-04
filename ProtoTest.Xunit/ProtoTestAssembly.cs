namespace ProtoTest.Xunit;

using global::Xunit;
using ProtoTest.Core;

/// <summary>
/// Base fixture for global assembly setup in xUnit v2.
/// Initializes and disposes the root <see cref="ProtoHost"/>.
/// </summary>
public abstract class ProtoTestAssembly : IAsyncLifetime
{
    private static ProtoHost? _host;

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Ensure your collection fixture inherits from ProtoTestAssembly.");

    public async Task InitializeAsync()
    {
        var builder = new ProtoHostBuilder();
        Configure(builder);
        _host = builder.Build();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services and hooks.
    /// </summary>
    protected abstract void Configure(IProtoHostBuilder builder);
}