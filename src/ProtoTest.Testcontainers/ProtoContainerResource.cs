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
    private TContainer? _container;
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

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            var container = _build();
            _container = container;
            _start(container, cancellationToken).GetAwaiter().GetResult();
            ConnectionString = _connectionString(container);
            return ValueTask.CompletedTask;
        }
        catch
        {
            // A failed start can be retried or reported; release the built container and stay releasable.
            Interlocked.Exchange(ref _started, 0);
            var failed = _container;
            _container = default;
            if (failed is not null)
            {
                try
                {
                    failed.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    // The start failure is what the caller needs to see.
                }
            }

            throw;
        }
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
