namespace ProtoTest.Core;

/// <summary>The completion of an operation whose entry is written by someone else.</summary>
internal sealed record ProtoTraceCompletion(
    ProtoTraceOutcome Outcome,
    Exception? Exception,
    IReadOnlyList<ProtoTraceSection> Sections);

/// <summary>A timed operation in a ProtoTest execution trace.</summary>
public sealed class ProtoTraceOperation : IDisposable
{
    private readonly ProtoTestTraceRecorder? _recorder;
    private readonly ProtoTestTraceRecorder.TraceEntryState? _entry;
    private readonly Action<ProtoTraceCompletion>? _complete;
    private readonly List<ProtoTraceSection>? _sections;
    private int _completed;

    internal ProtoTraceOperation(
        ProtoTestTraceRecorder recorder,
        ProtoTestTraceRecorder.TraceEntryState entry)
    {
        _recorder = recorder;
        _entry = entry;
    }

    /// <summary>Creates an operation that reports its completion to a callback instead of a recorder.</summary>
    internal ProtoTraceOperation(Action<ProtoTraceCompletion> complete)
    {
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        _sections = [];
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

    /// <summary>
    /// Adds a section to the operation: facts, a payload, check results or a diff. Sections are how an
    /// integration describes its operation; the viewer renders them without knowing the integration.
    /// </summary>
    public ProtoTraceOperation AddSection(ProtoTraceSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (_entry is not null)
        {
            _entry.AddSection(section);
        }
        else
        {
            _sections?.Add(section);
        }

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

        _complete?.Invoke(new ProtoTraceCompletion(outcome, exception, _sections ?? []));
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
