namespace ProtoTest.Core;

/// <summary>A timed operation in a ProtoTest execution trace.</summary>
public sealed class ProtoTraceOperation : IDisposable
{
    private readonly ProtoTestTraceRecorder? _recorder;
    private readonly ProtoTestTraceRecorder.TraceEntryState? _entry;
    private readonly Action<ProtoTraceOutcome, Exception?>? _complete;
    private int _completed;

    internal ProtoTraceOperation(
        ProtoTestTraceRecorder recorder,
        ProtoTestTraceRecorder.TraceEntryState entry)
    {
        _recorder = recorder;
        _entry = entry;
    }

    /// <summary>Creates an operation that reports its completion to a callback instead of a recorder.</summary>
    internal ProtoTraceOperation(Action<ProtoTraceOutcome, Exception?> complete)
    {
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    internal ProtoTraceOperation()
    {
    }

    public string Id => _entry?.Id ?? string.Empty;

    public ProtoTraceOperation SetAttribute(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _entry?.SetAttribute(name, value);
        return this;
    }

    public void Succeed() => Complete(ProtoTraceOutcome.Succeeded);

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Complete(ProtoTraceOutcome.Failed, exception);
    }

    public void Cancel(Exception? exception = null)
        => Complete(ProtoTraceOutcome.Cancelled, exception);

    public void Complete(ProtoTraceOutcome outcome, Exception? exception = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        if (_recorder is not null)
        {
            _recorder.Complete(_entry!, outcome, exception);
            return;
        }

        _complete?.Invoke(outcome, exception);
    }

    internal void Complete(ProtoTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        _recorder?.Complete(_entry!, result);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            Complete(ProtoTraceOutcome.Unknown);
        }
    }
}
