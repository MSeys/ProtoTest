namespace ProtoTest.Testcontainers;

using ProtoTest.Core;

/// <summary>
/// Base for a container owned by the run. The host starts it before the run, the started connection
/// string fills a configuration key so tests and an in-process application see the same value, and the
/// run releases it after the reports are written. Technology packages supply the builder and the
/// connection accessor; the base depends on no container library.
/// </summary>
public abstract class ProtoContainerResource<TContainer> : IProtoConnectionInfrastructure, IAsyncDisposable
    where TContainer : IAsyncDisposable
{
    private readonly Func<TContainer> _build;
    private readonly Func<TContainer, CancellationToken, Task> _start;
    private readonly Func<TContainer, string> _connectionString;
    private readonly object _containerGate = new();
    private TContainer? _container;
    private Task? _startTask;
    private int _started;
    private int _released;

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

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _released) != 0)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }

        await GetOrStartTask(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the one start task every caller awaits. Concurrent callers share the first task, so a
    /// second caller cannot return before the container is up and its connection string is set. A
    /// failed task is cleared so the resource can be retried.
    /// </summary>
    private Task GetOrStartTask(CancellationToken cancellationToken)
    {
        lock (_containerGate)
        {
            if (Volatile.Read(ref _released) != 0)
            {
                return Task.FromException(new ObjectDisposedException(GetType().FullName));
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
            var connectionString = _connectionString(container);

            lock (_containerGate)
            {
                if (Volatile.Read(ref _released) == 0)
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
    /// Starts a resource a caller just built, reporting why it could not start instead of throwing - a
    /// machine without a container runtime should be able to fall back or skip rather than fail the run.
    /// The asynchronous start runs on the thread pool, so a synchronous caller cannot deadlock on its
    /// own synchronization context while the shared start task completes. <see cref="StartAsync"/>
    /// already releases the built container when starting fails, so the candidate stays retryable; the
    /// caller can adopt it, retry, or simply let it go.
    /// </summary>
    protected static bool TryStartContainer(ProtoContainerResource<TContainer> candidate, out string? error)
    {
        try
        {
            Task.Run(() => candidate.StartAsync().AsTask()).GetAwaiter().GetResult();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        TContainer? container;
        lock (_containerGate)
        {
            container = _container;
            _container = default;
            Volatile.Write(ref _started, 0);
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
