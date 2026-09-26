namespace ProtoTest.Web.Selenium;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>
/// Runs every WebDriver call on one dedicated thread. Selenium's driver objects are not thread-safe
/// and their calls block, so the backend does not scatter them over the thread pool: work items queue
/// to a single pump, and a test's awaits never run driver code on a pool thread. Disposal is bounded:
/// the pump gets <see cref="_joinTimeout"/> to drain, and a pump still running reports the work it was
/// executing instead of abandoning the thread silently.
/// </summary>
internal sealed class SeleniumDriverExecutor : IAsyncDisposable
{
    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Thread _pump;
    private readonly TimeSpan _joinTimeout;
    private string? _currentWork;

    public SeleniumDriverExecutor(TimeSpan? joinTimeout = null)
    {
        _joinTimeout = joinTimeout ?? TimeSpan.FromSeconds(5);
        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "prototest-selenium-driver"
        };
        _pump.Start();
    }

    /// <summary>The caller name of the work item the pump is executing, or null when it is idle.</summary>
    public string? CurrentWork => Volatile.Read(ref _currentWork);

    /// <summary>Queues one driver call and completes with its result, or cancels before it starts.</summary>
    public ValueTask<T> RunAsync<T>(
        Func<T> work,
        CancellationToken cancellationToken,
        [CallerMemberName] string? workName = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<T>(cancellationToken);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(() => Run(work, completion, cancellationToken, workName ?? "driver call")))
        {
            completion.TrySetException(new ObjectDisposedException(nameof(SeleniumDriverExecutor)));
        }

        return new ValueTask<T>(completion.Task);
    }

    public async ValueTask RunAsync(Action work, CancellationToken cancellationToken, [CallerMemberName] string? workName = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        await RunAsync(() => { work(); return true; }, cancellationToken, workName);
    }

    /// <summary>
    /// Completes the queue and waits up to the join timeout for the pump. Returns the work the pump was
    /// still executing when the bound expired, or <see langword="null"/> when it stopped in time.
    /// </summary>
    public async ValueTask<string?> StopAsync()
    {
        _queue.Writer.TryComplete();
        var joined = await Task.Run(() => _pump.Join(_joinTimeout));
        return joined ? null : CurrentWork ?? "a driver call";
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void Run<T>(
        Func<T> work,
        TaskCompletionSource<T> completion,
        CancellationToken cancellationToken,
        string workName)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        Volatile.Write(ref _currentWork, workName);
        try
        {
            completion.TrySetResult(work());
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            Volatile.Write(ref _currentWork, null);
        }
    }

    private void Pump()
    {
        foreach (var work in _queue.Reader.ReadAllAsync().ToBlockingEnumerable())
        {
            work();
        }
    }
}
