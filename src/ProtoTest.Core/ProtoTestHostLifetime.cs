namespace ProtoTest.Core;

/// <summary>
/// Owns the single root <see cref="ProtoHost"/> for a test assembly and enforces one-time
/// initialization. Shared by every test-framework adapter so host lifetime, guards, and shutdown
/// ordering stay identical across frameworks.
/// </summary>
public sealed class ProtoTestHostLifetime
{
    // The flow that is inside StartCoreAsync. Stop on that same flow would wait for the start
    // while the start waits for the stop.
    private static readonly AsyncLocal<ProtoTestHostLifetime?> ActiveStart = new();

    private readonly ProtoLock _gate = new();
    private readonly string _initializationHint;
    private ProtoHost? _host;
    private Task<ProtoHost>? _starting;
    private bool _stopRequested;

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
    /// flight; a failed start leaves the lifetime retryable. A <see cref="StopAsync"/> that arrives
    /// while this start is in flight still lets the start finish, then stops the host; this call then
    /// throws, so the caller does not observe a start that no longer has a host.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The host was already initialized, a start is already in flight, or it was stopped while starting.
    /// </exception>
    public Task StartAsync(Action<IProtoHostBuilder> configure)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(configure);
            Task<ProtoHost> attempt;
            // Set for the start's own flow, then put the caller's flow back before returning. The start
            // captures the value at its first await; the caller waiting on this task must still be able to stop.
            var previous = ActiveStart.Value;
            ActiveStart.Value = this;
            try
            {
                lock (_gate)
                {
                    if (_host is not null || _starting is not null)
                    {
                        throw new InvalidOperationException("ProtoHost has already been initialized for this assembly.");
                    }

                    attempt = StartCoreAsync(configure);
                    _starting = attempt;
                }
            }
            finally
            {
                ActiveStart.Value = previous;
            }

            return CompleteStartAsync(attempt);
        }
        catch (Exception ex)
        {
            // Guards throw before the first await. Callers observe them on the returned task, as before.
            return Task.FromException(ex);
        }
    }

    private async Task CompleteStartAsync(Task<ProtoHost> attempt)
    {
        var completed = false;
        var stoppedWhileStarting = false;
        try
        {
            await attempt.ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            lock (_gate)
            {
                if (completed)
                {
                    stoppedWhileStarting = _stopRequested;
                }

                _stopRequested = false;
                if (ReferenceEquals(_starting, attempt))
                {
                    _starting = null;
                }
            }
        }

        if (stoppedWhileStarting)
        {
            throw new InvalidOperationException("The host was stopped while it was starting.");
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

    /// <summary>
    /// Stops and disposes the host, clearing it so later access reports "not initialized".
    /// A stop during an in-flight start waits for that start to finish, then stops the host
    /// when one was published. The start is not cancelled: <see cref="StartAsync"/> has no
    /// cancellation token. This method does not return while the start is still in flight,
    /// and it does not leave a host published afterwards. A stop from inside the start itself
    /// throws instead of waiting: that wait cannot finish while the start is still inside the caller.
    /// A start that fails while this method is waiting leaves no host; this method then returns
    /// without running AfterRun, and the start's exception still goes to the <see cref="StartAsync"/> caller.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called from inside the start it would wait for.
    /// </exception>
    public async Task StopAsync()
    {
        if (ReferenceEquals(ActiveStart.Value, this))
        {
            throw new InvalidOperationException("The host cannot be stopped from inside its own start.");
        }

        Task<ProtoHost>? starting;
        lock (_gate)
        {
            starting = _starting;
            if (starting is not null)
            {
                _stopRequested = true;
            }
        }

        if (starting is not null)
        {
            try
            {
                await starting.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A failed start publishes nothing and has already released what it started.
            }
        }

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
