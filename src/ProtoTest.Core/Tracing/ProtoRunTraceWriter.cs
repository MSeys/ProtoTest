namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using ProtoTest.Core.Internal;

/// <summary>
/// Records operations and events that belong to the run rather than to one test - run-scoped resources
/// being the first of them. Operations are written when they complete: a release still running while the
/// trace is archived is not yet part of it.
/// </summary>
internal sealed class ProtoRunTraceWriter : IProtoTraceWriter
{
    private static readonly IReadOnlyDictionary<string, string?> NoAttributes =
        new Dictionary<string, string?>();

    private readonly IReadOnlyCollection<string>? _additionalSensitiveNames;

    private readonly ConcurrentQueue<ProtoTraceEntry> _entries = new();
    private readonly ProtoItemStore _items = new();
    private readonly ProtoLock _recordGate = new();
    private readonly List<ProtoTraceObservationRecord> _observations = [];
    private readonly List<ProtoTraceAttachmentRecord> _attachments = [];
    private readonly List<ProtoTraceFindingRecord> _findings = [];
    private int _sequence;

    public ProtoRunTraceWriter(IReadOnlyCollection<string>? additionalSensitiveNames = null)
    {
        _additionalSensitiveNames = additionalSensitiveNames;
    }

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
        lock (_recordGate)
        {
            _observations.Add(ProtoTraceRecords.Observation(
                $"observation-{_observations.Count + 1}", null, targetName, kind, identifier, data, metadata));
        }
    }

    public void Attachment(string name, string mediaType, string? description = null)
    {
        lock (_recordGate)
        {
            _attachments.Add(ProtoTraceRecords.Attachment(
                $"attachment-{_attachments.Count + 1}", null, name, mediaType, description));
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
        lock (_recordGate)
        {
            _findings.Add(ProtoTraceRecords.Finding(
                $"finding-{_findings.Count + 1}",
                null,
                message,
                status,
                category,
                targetName,
                tags,
                ProtoMetadataRedaction.Redact(metadata, _additionalSensitiveNames)));
        }
    }

    public string? CaptureActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var attributes = ProtoTraceActivity.Attributes(activity);
        var failed = ProtoTraceActivity.IsFailure(activity);
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
            ProtoTraceActivity.Error(activity)));
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
