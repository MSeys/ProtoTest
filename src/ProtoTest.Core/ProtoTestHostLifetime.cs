namespace ProtoTest.Core;

/// <summary>
/// Owns the single root <see cref="ProtoHost"/> for a test assembly and enforces one-time
/// initialization. Shared by every test-framework adapter so host lifetime, guards, and shutdown
/// ordering stay identical across frameworks.
/// </summary>
public sealed class ProtoTestHostLifetime
{
    private readonly object _gate = new();
    private readonly string _initializationHint;
    private ProtoHost? _host;
    private Task<ProtoHost>? _starting;

    /// <param name="initializationHint">
    /// Adapter-specific guidance appended to the "not initialized" error message.
    /// </param>
    public ProtoTestHostLifetime(string initializationHint = "Initialize the ProtoTest assembly fixture before running tests.")
    {
        _initializationHint = initializationHint ?? string.Empty;
    }

    /// <summary>Gets the initialized host.</summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before initialization.</exception>
    public ProtoHost Host => Volatile.Read(ref _host)
        ?? throw new InvalidOperationException($"ProtoHost is not initialized. {_initializationHint}");

    /// <summary>
    /// Builds and starts the host once. Throws if it has already been initialized or a start is in
    /// flight; a failed start leaves the lifetime retryable.
    /// </summary>
    public async Task StartAsync(Action<IProtoHostBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Task<ProtoHost> attempt;
        lock (_gate)
        {
            if (_host is not null || _starting is not null)
            {
                throw new InvalidOperationException("ProtoHost has already been initialized for this assembly.");
            }

            attempt = StartCoreAsync(configure);
            _starting = attempt;
        }

        try
        {
            await attempt.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_starting, attempt))
                {
                    _starting = null;
                }
            }
        }
    }

    private async Task<ProtoHost> StartCoreAsync(Action<IProtoHostBuilder> configure)
    {
        var builder = new ProtoHostBuilder();
        configure(builder);
        var host = builder.Build();
        try
        {
            await host.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The start failure is what the caller needs to see; a disposal failure must not hide it.
            }

            throw;
        }

        lock (_gate)
        {
            _host = host;
        }

        return host;
    }

    /// <summary>Stops and disposes the host, clearing it so later access reports "not initialized".</summary>
    public async Task StopAsync()
    {
        ProtoHost? host;
        lock (_gate)
        {
            host = _host;
            _host = null;
        }

        if (host is null)
        {
            return;
        }

        try
        {
            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }
}
