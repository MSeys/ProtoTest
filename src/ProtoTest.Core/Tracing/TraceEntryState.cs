namespace ProtoTest.Core;

using System.Diagnostics;

internal sealed class TraceEntryState
{
    private readonly ProtoLock _gate = new();
    private readonly long _startedTimestamp;
    private readonly Dictionary<string, string?> _attributes;
    private int _completed;
    private int _count = 1;
    private TimeSpan? _duration;
    private ProtoTraceOutcome _outcome;
    private ProtoTraceError? _error;
    private readonly List<ProtoTraceSection> _sections = [];
    private readonly List<ProtoTraceObservationRecord> _recordObservations = [];
    private readonly List<ProtoTraceAttachmentRecord> _recordAttachments = [];
    private readonly List<ProtoTraceFindingRecord> _recordFindings = [];
    private readonly Activity? _activity;
    private bool _hasFailedDescendant;

    public TraceEntryState(
        string id,
        string? parentId,
        ProtoTraceEntryKind entryKind,
        string kind,
        string name,
        string source,
        ProtoTracePhase phase,
        DateTimeOffset timestampUtc,
        long startedTimestamp,
        IReadOnlyDictionary<string, string?>? attributes,
        TraceEntryState? parent,
        Activity? activity = null,
        string? entityKind = null,
        string? entityId = null)
    {
        Id = id;
        ParentId = parentId;
        EntryKind = entryKind;
        Kind = kind;
        Name = name;
        Source = source;
        Phase = phase;
        TimestampUtc = timestampUtc;
        _startedTimestamp = startedTimestamp;
        _attributes = attributes is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(attributes, StringComparer.Ordinal);
        Parent = parent;
        _activity = activity;
        EntityKind = entityKind;
        EntityId = entityId;
    }

    public string Id { get; }
    public string? ParentId { get; }
    public ProtoTraceEntryKind EntryKind { get; }
    public string Kind { get; }
    public string Name { get; }
    public string Source { get; }
    public ProtoTracePhase Phase { get; }
    public DateTimeOffset TimestampUtc { get; }
    public TraceEntryState? Parent { get; }
    public string? EntityKind { get; }
    public string? EntityId { get; }
    public Activity? Activity => _activity;
    public ProtoTraceOutcome Outcome { get { lock (_gate) return _outcome; } }
    public int Count { get { lock (_gate) return _count; } }

    /// <summary>
    /// Whether any descendant completed failed or partial, recorded as it happens so an operation's
    /// outcome does not need a scan of every entry when it completes.
    /// </summary>
    public bool HasFailedDescendant { get { lock (_gate) return _hasFailedDescendant; } }

    public void MarkFailedDescendant()
    {
        lock (_gate) _hasFailedDescendant = true;
    }
    public IReadOnlyDictionary<string, string?> Attributes { get { lock (_gate) return new Dictionary<string, string?>(_attributes); } }

    public void SetAttribute(string name, string? value)
    {
        lock (_gate) _attributes[name] = value;
    }

    public void SetCount(int count)
    {
        lock (_gate) _count = count;
    }

    public void AddSection(ProtoTraceSection section)
    {
        lock (_gate) _sections.Add(section);
    }

    public void AddObservation(
        string targetName,
        string kind,
        string identifier,
        string? data,
        string? metadata)
    {
        var record = ProtoTraceRecords.Observation(string.Empty, Id, targetName, kind, identifier, data, metadata);
        lock (_gate)
        {
            _recordObservations.Add(record);
        }
    }

    public void AddAttachment(string name, string mediaType, string? description)
    {
        var record = ProtoTraceRecords.Attachment(string.Empty, Id, name, mediaType, description);
        lock (_gate)
        {
            _recordAttachments.Add(record);
        }
    }

    public void AddFinding(
        string message,
        string status,
        string category,
        string? targetName,
        IReadOnlyList<string>? tags,
        IReadOnlyDictionary<string, object>? metadata)
    {
        var record = ProtoTraceRecords.Finding(string.Empty, Id, message, status, category, targetName, tags, metadata);
        lock (_gate)
        {
            _recordFindings.Add(record);
        }
    }

    public void CollectRecord(
        List<ProtoTraceObservationRecord> observations,
        List<ProtoTraceAttachmentRecord> attachments,
        List<ProtoTraceFindingRecord> findings)
    {
        lock (_gate)
        {
            observations.AddRange(_recordObservations);
            attachments.AddRange(_recordAttachments);
            findings.AddRange(_recordFindings);
        }
    }

    public bool TryPatchAttachment(
        string name,
        string artifactId,
        string? archivePath,
        long sizeBytes,
        string? error)
    {
        lock (_gate)
        {
            var index = _recordAttachments.FindLastIndex(attachment =>
                string.Equals(attachment.Name, name, StringComparison.Ordinal)
                && attachment.ArtifactId is null);
            if (index < 0)
            {
                return false;
            }

            _recordAttachments[index] = _recordAttachments[index] with
            {
                ArtifactId = artifactId,
                ArchivePath = archivePath,
                SizeBytes = sizeBytes,
                Error = error
            };
            return true;
        }
    }

    public void Complete(
        ProtoTraceOutcome outcome,
        Exception? exception,
        ProtoTraceError? error = null,
        TimeSpan? duration = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        lock (_gate)
        {
            _outcome = outcome;
            _error = error ?? (exception is null ? null : ProtoTraceError.FromException(exception));
            if (EntryKind == ProtoTraceEntryKind.Operation)
                _duration = duration ?? Stopwatch.GetElapsedTime(_startedTimestamp);
        }
        CompleteActivity(outcome, exception, error);
    }

    private void CompleteActivity(
        ProtoTraceOutcome outcome,
        Exception? exception,
        ProtoTraceError? error)
    {
        if (_activity is null) return;
        _activity.SetTag("prototest.outcome", outcome.ToString().ToLowerInvariant());
        foreach (var (key, value) in Attributes)
            if (ProtoTestTraceRecorder.ShouldExportTag(key, value)) _activity.SetTag(key, value);
        var traceError = error ?? (exception is null ? null : ProtoTraceError.FromException(exception));
        if (traceError is not null)
        {
            _activity.SetStatus(ActivityStatusCode.Error, traceError.Message);
            _activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"] = traceError.Type,
                ["exception.message"] = traceError.Message,
                ["exception.stacktrace"] = traceError.StackTrace
            }));
        }
        else if (outcome == ProtoTraceOutcome.Succeeded) _activity.SetStatus(ActivityStatusCode.Ok);
        _activity.Stop();
    }

    public void CompleteIfOpen(ProtoTraceOutcome outcome) => Complete(outcome, null);

    public ProtoTraceEntry Snapshot()
    {
        lock (_gate)
        {
            return new ProtoTraceEntry(
                Id,
                ParentId,
                EntryKind,
                Kind,
                Name,
                Source,
                Phase,
                TimestampUtc,
                _duration,
                _outcome,
                new Dictionary<string, string?>(_attributes, StringComparer.Ordinal),
                _error,
                EntityKind,
                EntityId,
                _count,
                _sections.Count == 0 ? null : [.. _sections]);
        }
    }
}

