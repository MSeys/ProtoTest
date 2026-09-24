namespace ProtoTest.Web.Selenium;

using System.Threading.Channels;

/// <summary>
/// Runs every WebDriver call on one dedicated thread. Selenium's driver objects are not thread-safe
/// and their calls block, so the backend does not scatter them over the thread pool: work items queue
/// to a single pump, and a test's awaits never run driver code on a pool thread.
/// </summary>
internal sealed class SeleniumDriverExecutor : IAsyncDisposable
{
    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Thread _pump;

    public SeleniumDriverExecutor()
    {
        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "prototest-selenium-driver"
        };
        _pump.Start();
    }

    /// <summary>Queues one driver call and completes with its result, or cancels before it starts.</summary>
    public ValueTask<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<T>(cancellationToken);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(() => Run(work, completion, cancellationToken)))
        {
            completion.TrySetException(new ObjectDisposedException(nameof(SeleniumDriverExecutor)));
        }

        return new ValueTask<T>(completion.Task);
    }

    public async ValueTask RunAsync(Action work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await RunAsync(() => { work(); return true; }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await Task.Run(() => _pump.Join(TimeSpan.FromSeconds(5)));
    }

    private static void Run<T>(Func<T> work, TaskCompletionSource<T> completion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

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
    }

    private void Pump()
    {
        foreach (var work in _queue.Reader.ReadAllAsync().ToBlockingEnumerable())
        {
            work();
        }
    }
}
