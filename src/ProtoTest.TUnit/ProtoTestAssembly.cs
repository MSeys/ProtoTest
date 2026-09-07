namespace ProtoTest.TUnit;

using ProtoTest.Core;

/// <summary>
/// Abstract base class for TUnit assembly configuration.
/// Manages the root <see cref="ProtoHost"/> lifecycle for the assembly.
/// </summary>
public abstract class ProtoTestAssembly
{
    private static ProtoHost? _host;

    /// <summary>
    /// Gets the initialized global <see cref="ProtoHost"/> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before host initialization.</exception>
    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Ensure your setup class inherits from ProtoTestAssembly.");

    /// <summary>
    /// Builds and starts the global <see cref="ProtoHost"/> instance asynchronously.
    /// </summary>
    /// <param name="configure">Delegate to configure the <see cref="IProtoHostBuilder"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown if the host has already been initialized.</exception>
    protected static async Task InitializeAsync(Action<IProtoHostBuilder> configure)
    {
        if (_host != null)
        {
            throw new InvalidOperationException("ProtoHost has already been initialized for this assembly.");
        }

        var builder = new ProtoHostBuilder();
        configure(builder);
        _host = builder.Build();

        await _host.StartAsync();
    }

    /// <summary>
    /// Stops and disposes the global <see cref="ProtoHost"/> instance asynchronously.
    /// </summary>
    protected static async Task CleanupAsync()
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
}