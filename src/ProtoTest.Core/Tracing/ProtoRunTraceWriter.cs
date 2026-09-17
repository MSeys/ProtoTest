namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Records operations and events that belong to the run rather than to one test - run-scoped resources
/// being the first of them. Entries are written when they complete, so a release that is still running
/// when the trace is archived appears as an unfinished operation rather than disappearing.
/// </summary>
internal sealed class ProtoRunTraceWriter : IProtoTraceWriter
{
    private static readonly IReadOnlyDictionary<string, string?> NoAttributes =
        new Dictionary<string, string?>();

    private readonly ConcurrentQueue<ProtoTraceEntry> _entries = new();
    private int _sequence;

    public IReadOnlyList<ProtoTraceEntry> Snapshot()
        => [.. _entries.OrderBy(entry => entry.TimestampUtc)];

    public ProtoTraceOperation StartOperation(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
    {
        var id = $"run-{Interlocked.Increment(ref _sequence)}";
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        return new ProtoTraceOperation((outcome, exception) =>
        {
            stopwatch.Stop();
            _entries.Enqueue(new ProtoTraceEntry(
                id,
                parentId,
                ProtoTraceEntryKind.Operation,
                kind,
                name,
                source,
                phase,
                startedAtUtc,
                stopwatch.Elapsed,
                outcome,
                attributes ?? NoAttributes,
                exception is null ? null : ProtoTraceError.FromException(exception)));
        });
    }

    public void WriteEvent(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        ProtoTraceOutcome outcome = ProtoTraceOutcome.Unknown,
        IReadOnlyDictionary<string, string?>? attributes = null,
        Exception? exception = null,
        string? parentId = null)
        => _entries.Enqueue(new ProtoTraceEntry(
            $"run-{Interlocked.Increment(ref _sequence)}",
            parentId,
            ProtoTraceEntryKind.Event,
            kind,
            name,
            source,
            phase,
            DateTimeOffset.UtcNow,
            null,
            outcome,
            attributes ?? NoAttributes,
            exception is null ? null : ProtoTraceError.FromException(exception)));
}
