namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

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
    private readonly ProtoItemStore _items = new();
    private readonly ProtoLock _recordGate = new();
    private readonly List<ProtoTraceObservationRecord> _observations = [];
    private readonly List<ProtoTraceAttachmentRecord> _attachments = [];
    private readonly List<ProtoTraceFindingRecord> _findings = [];
    private int _sequence;

    public IReadOnlyList<ProtoTraceEntry> Snapshot()
        => [.. _entries.OrderBy(entry => entry.TimestampUtc)];

    public IReadOnlyList<ProtoTraceEntity> SnapshotEntities() => _items.SnapshotEntities();

    public IReadOnlyList<ProtoTraceValue> SnapshotValues() => _items.SnapshotValues();

    public ProtoTraceRecord? SnapshotRecord()
    {
        lock (_recordGate)
        {
            if (_observations.Count == 0 && _attachments.Count == 0 && _findings.Count == 0)
            {
                return null;
            }

            return new ProtoTraceRecord(
                _observations.Count == 0 ? null : [.. _observations],
                _attachments.Count == 0 ? null : [.. _attachments],
                _findings.Count == 0 ? null : [.. _findings]);
        }
    }

    public void Observation(
        string targetName,
        string kind,
        string identifier,
        string? data = null,
        string? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        lock (_recordGate)
        {
            _observations.Add(new ProtoTraceObservationRecord(
                $"observation-{_observations.Count + 1}",
                null,
                DateTimeOffset.UtcNow,
                targetName,
                kind,
                identifier,
                data,
                metadata));
        }
    }

    public void Attachment(string name, string mediaType, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        lock (_recordGate)
        {
            _attachments.Add(new ProtoTraceAttachmentRecord(
                $"attachment-{_attachments.Count + 1}",
                null,
                DateTimeOffset.UtcNow,
                name,
                mediaType,
                description));
        }
    }

    public void Finding(
        string message,
        string status,
        string category,
        string? targetName = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        lock (_recordGate)
        {
            _findings.Add(new ProtoTraceFindingRecord(
                $"finding-{_findings.Count + 1}",
                null,
                DateTimeOffset.UtcNow,
                message,
                status,
                category,
                targetName,
                tags,
                metadata));
        }
    }

    public string? CaptureActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["activity.source"] = activity.Source.Name
        };
        foreach (var tag in activity.TagObjects)
        {
            attributes[tag.Key] = tag.Value?.ToString();
        }

        var failed = activity.Status == ActivityStatusCode.Error;
        var id = $"run-{Interlocked.Increment(ref _sequence)}";
        _entries.Enqueue(new ProtoTraceEntry(
            id,
            ParentId: null,
            ProtoTraceEntryKind.Operation,
            activity.Source.Name,
            activity.DisplayName,
            activity.Source.Name,
            ProtoTracePhase.Run,
            activity.StartTimeUtc,
            activity.Duration,
            failed ? ProtoTraceOutcome.Failed : ProtoTraceOutcome.Succeeded,
            attributes,
            failed && activity.StatusDescription is { Length: > 0 } description
                ? new ProtoTraceError("ActivityError", description)
                : null));
        return id;
    }

    public ProtoTraceOperation StartOperation(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null,
        string? entityKind = null,
        string? entityId = null)
    {
        var id = $"run-{Interlocked.Increment(ref _sequence)}";
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        return new ProtoTraceOperation(completion =>
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
                completion.Outcome,
                attributes ?? NoAttributes,
                completion.Exception is null ? null : ProtoTraceError.FromException(completion.Exception),
                entityKind,
                entityId,
                Sections: completion.Sections.Count == 0 ? null : [.. completion.Sections]));
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
        string? parentId = null,
        string? entityKind = null,
        string? entityId = null)
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
            exception is null ? null : ProtoTraceError.FromException(exception),
            entityKind,
            entityId));

    public void SetEntityState(
        string kind,
        string id,
        string name,
        IReadOnlyDictionary<string, string?>? state = null,
        string? scope = null,
        string? change = null)
        => _items.SetState(kind, id, name, state, scope, change, operationId: null);

    public void Value(
        string kind,
        string id,
        string name,
        string change,
        IReadOnlyDictionary<string, string?>? state = null,
        ProtoTraceValueSource source = ProtoTraceValueSource.TestSide,
        bool inferred = false,
        string? scope = null,
        string? operationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(change);
        _items.AddValue(kind, id, name, change, operationId, state, source, inferred, scope);
    }
}
