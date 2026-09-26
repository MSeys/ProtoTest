namespace ProtoTest.Testcontainers;

using ProtoTest.Core;

/// <summary>
/// Base for a container owned by the run. The host starts it before the run, the started connection
/// string fills a configuration key so tests and an in-process application see the same value, and the
/// run releases it after the reports are written. Technology packages supply the builder and the
/// connection accessor; the base depends on no container library.
/// </summary>
public abstract class ProtoContainerResource<TContainer> : IProtoConnectionInfrastructure, IProtoConfiguredInfrastructure, IProtoStartupEvidence, IAsyncDisposable
    where TContainer : IAsyncDisposable
{
    private readonly Func<TContainer> _build;
    private readonly Func<TContainer, CancellationToken, Task> _start;
    private readonly Func<TContainer, string> _connectionString;
    private readonly List<(string Name, Func<TContainer, CancellationToken, ValueTask<bool>> Check)> _readiness = [];
    private readonly object _containerGate = new();
    private TContainer? _container;
    private Task? _startTask;
    private int _started;
    private int _released;
    private int? _readinessPort;
    private ProtoReadinessOptions? _readinessOptions;
    private Dictionary<string, string?> _startupEvidence = new(StringComparer.Ordinal);

    protected ProtoContainerResource(
        Func<TContainer> build,
        Func<TContainer, CancellationToken, Task> start,
        Func<TContainer, string> connectionString)
    {
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public abstract string Id { get; }

    public abstract string Kind { get; }

    public abstract string Description { get; }

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    /// <summary>Gets the connection string; empty until the container started.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Gets what the readiness checks observed while the container started.</summary>
    public IReadOnlyDictionary<string, string?> StartupEvidence => _startupEvidence;

    /// <summary>
    /// Gets or sets how long the readiness checks may take after the container starts when the
    /// container is started directly. Defaults to 30 seconds; a technology package may tighten it.
    /// When the host starts the container, the run's <see cref="ProtoReadinessOptions"/> wins.
    /// </summary>
    protected TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the pause between readiness checks. Defaults to 100 milliseconds.</summary>
    protected TimeSpan ReadinessInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Registers a readiness check evaluated against the started container before the host moves on;
    /// use <see cref="ReadyWhenTcp"/> for the common "the endpoint answers" case. A check that never
    /// becomes ready fails the start with the probe's name, and the container is released like any
    /// failed start.
    /// </summary>
    protected void ReadyWhen(string name, Func<TContainer, CancellationToken, ValueTask<bool>> ready)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(ready);
        _readiness.Add((name, ready));
    }

    /// <summary>
    /// Declares the container's default readiness check: a TCP connection to the endpoint the
    /// <paramref name="endpoint"/> resolver returns for the image's standard
    /// <paramref name="defaultPort"/> must succeed. Override the port with <see cref="ReadyOn"/> when
    /// the image listens elsewhere.
    /// </summary>
    protected void ReadyWhenTcp(
        string name,
        int defaultPort,
        Func<TContainer, int, (string Host, int Port)> endpoint)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultPort, 1);
        ArgumentNullException.ThrowIfNull(endpoint);
        ReadyWhen(name, (container, cancellationToken) =>
        {
            var (host, port) = endpoint(container, EffectiveReadinessPort(defaultPort));
            return ProtoReadiness.Tcp(host, port)(cancellationToken);
        });
    }

    /// <summary>
    /// Gets the port a readiness check should use: the one <see cref="ReadyOn"/> set, or the image's
    /// standard port.
    /// </summary>
    protected int EffectiveReadinessPort(int defaultPort) => _readinessPort ?? defaultPort;

    /// <summary>
    /// Sets the container port the default readiness check waits for, for images that do not listen on
    /// their standard port. Returns the resource for chaining; call it before the run starts.
    /// </summary>
    public ProtoContainerResource<TContainer> ReadyOn(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        if (IsStarted)
        {
            throw new InvalidOperationException(
                "The container already started; set the readiness port before the run starts.");
        }

        _readinessPort = port;
        return this;
    }

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await GetOrStartTask(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the piece with the run's collected state, so the run's readiness policy
    /// (<see cref="ProtoInfrastructureContext.Readiness"/>, set by <c>ConfigureReadiness</c> or
    /// <c>ProtoTest:Readiness</c>) governs this container's waits like every host probe. The host
    /// prefers this overload; a direct <see cref="StartAsync(CancellationToken)"/> uses the
    /// container's own <see cref="ReadinessTimeout"/>/<see cref="ReadinessInterval"/>.
    /// </summary>
    public ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _readinessOptions = context.Readiness;
        return StartAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the one start task every caller awaits. Concurrent callers share the first task, so a
    /// second caller cannot return before the container is up and its connection string is set. A
    /// failed task is cleared so the resource can be retried, and a released resource starts a fresh
    /// container: the retry after a failed run start re-owns the infrastructure it starts again, so
    /// the new ownership period must be releasable too.
    /// </summary>
    private Task GetOrStartTask(CancellationToken cancellationToken)
    {
        lock (_containerGate)
        {
            // Only a settled release re-arms the resource; a release racing an in-flight start lets
            // that start observe the release and fault instead of adopting a container nothing owns.
            if (_startTask is null && _released != 0)
            {
                _released = 0;
            }

            if (_startTask is null)
            {
                // The completion source is cached before the start body runs, so a failure that
                // happens synchronously can clear the cache without the assignment restoring it.
                // The task is captured locally too: the body must not be able to null the return.
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task = completion.Task;
                _startTask = task;
                _ = StartAndAdoptAsync(completion, cancellationToken);
                return task;
            }

            return _startTask;
        }
    }

    private async Task StartAndAdoptAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        TContainer? container = default;
        try
        {
            // The build runs inside the try: a throwing builder must not mark the resource started,
            // or the failure would wedge it with no way to retry.
            container = _build();

            // Awaiting instead of blocking keeps the caller's synchronization context free; a UI or
            // single-threaded host must not deadlock on a container that starts on another thread.
            await _start(container, cancellationToken).ConfigureAwait(false);
            await AwaitReadinessAsync(container, cancellationToken).ConfigureAwait(false);
            var connectionString = _connectionString(container);

            lock (_containerGate)
            {
                if (_released == 0)
                {
                    ConnectionString = connectionString;
                    _container = container;
                    Volatile.Write(ref _started, 1);
                    completion.SetResult();
                    return;
                }
            }

            // Disposal won the race while the container started and cannot have seen the container
            // yet; release it here.
            ResetStartTask();
            await container.DisposeAsync().ConfigureAwait(false);
            completion.SetException(new ObjectDisposedException(GetType().FullName));
        }
        catch (Exception exception)
        {
            // A failed start can be retried or reported; release the built container and stay releasable.
            ResetStartTask();
            if (container is not null)
            {
                try
                {
                    await container.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // The start failure is what the caller needs to see.
                }
            }

            completion.SetException(exception);
        }
    }

    private void ResetStartTask()
    {
        lock (_containerGate)
        {
            _startTask = null;
            Volatile.Write(ref _started, 0);
        }
    }

    /// <summary>
    /// Runs the registered readiness checks against the started container and records what each
    /// observed, so the trace shows the wait instead of an opaque start duration.
    /// </summary>
    private async Task AwaitReadinessAsync(TContainer container, CancellationToken cancellationToken)
    {
        if (_readiness.Count == 0)
        {
            return;
        }

        // One timeout owner: the run's policy when the piece started through a host, the container's
        // own values when it was started directly.
        var timeout = _readinessOptions?.Timeout ?? ReadinessTimeout;
        var interval = _readinessOptions?.Interval ?? ReadinessInterval;
        var evidence = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, check) in _readiness)
        {
            var result = await ProtoReadiness
                .WaitAsync(name, token => check(container, token), timeout, interval, cancellationToken)
                .ConfigureAwait(false);
            evidence[$"readiness.{name}.attempts"] = result.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
            evidence[$"readiness.{name}.waitedMs"] = ((long)result.Waited.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        _startupEvidence = evidence;
    }

    /// <summary>
    /// Starts a resource a caller just built, reporting why it could not start instead of throwing - a
    /// machine without a container runtime should be able to fall back or skip rather than fail the run.
    /// The asynchronous start runs on the thread pool, so a synchronous caller cannot deadlock on its
    /// own synchronization context while the shared start task completes. <see cref="StartAsync"/>
    /// already releases the built container when starting fails, so the candidate stays retryable; the
    /// caller can adopt it, retry, or simply let it go.
    /// </summary>
    protected static ContainerStartResult<TResource> TryStartContainer<TResource>(TResource resource)
        where TResource : ProtoContainerResource<TContainer>
    {
        ArgumentNullException.ThrowIfNull(resource);
        try
        {
            Task.Run(() => resource.StartAsync().AsTask()).GetAwaiter().GetResult();
            return new(resource, null);
        }
        catch (Exception exception)
        {
            return new(null, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ReleaseCoreAsync(context.Trace, context.CancellationToken);
    }

    public ValueTask DisposeAsync() => ReleaseCoreAsync(trace: null, cancellationToken: CancellationToken.None);

    /// <summary>How long a release waits for an in-flight start before recording it as abandoned.</summary>
    private static readonly TimeSpan AbandonedStartBound = TimeSpan.FromSeconds(5);

    private async ValueTask ReleaseCoreAsync(IProtoTraceWriter? trace, CancellationToken cancellationToken)
    {
        TContainer? container;
        Task? inFlight;
        lock (_containerGate)
        {
            if (_released != 0)
            {
                return;
            }

            _released = 1;
            container = _container;
            _container = default;
            // The released container's endpoint is dead; nothing may keep reading it.
            ConnectionString = string.Empty;
            Volatile.Write(ref _started, 0);

            // A settled start is replaced by the next ownership period's start; an in-flight start
            // is awaited below, bounded, so a stuck start cannot hang the release.
            inFlight = _startTask;
            if (_startTask is { IsCompletedSuccessfully: true })
            {
                _startTask = null;
            }
        }

        if (container is not null)
        {
            await container.DisposeAsync().ConfigureAwait(false);
        }

        if (inFlight is null || inFlight.IsCompleted)
        {
            return;
        }

        // Release must terminate: wait for the racing start under a small bound, then record the start
        // that outlived its release instead of returning as if nothing were still running (audit A5-55).
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(AbandonedStartBound);
        try
        {
            await inFlight.WaitAsync(bound.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && bound.IsCancellationRequested)
            {
                trace?.WriteEvent(
                    "container.start.abandoned",
                    $"Container start abandoned after {AbandonedStartBound.TotalSeconds:0.#}s",
                    "ProtoTest.Testcontainers",
                    ProtoTracePhase.Teardown,
                    ProtoTraceOutcome.Failed,
                    new Dictionary<string, string?>
                    {
                        ["container.id"] = Id,
                        ["container.type"] = GetType().Name,
                        ["container.release_bound_ms"] = AbandonedStartBound.TotalMilliseconds.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    },
                    exception);
                return;
            }

            // The racing start's own failure (the release-won ObjectDisposedException, say) was already
            // reported to whoever awaited the start; the release does not adopt it.
        }
    }
}
