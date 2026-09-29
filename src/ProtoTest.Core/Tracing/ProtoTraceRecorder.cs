namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using ProtoTest.Core.Internal;

internal sealed class ProtoTestTraceRecorder : IProtoTraceWriter
{
    private readonly ConcurrentQueue<TraceEntryState> _entries = new();
    private readonly ConcurrentDictionary<string, TraceEntryState> _entriesById = new(StringComparer.Ordinal);
    private readonly AsyncLocal<TraceEntryState?> _current = new();
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly string? _className;
    private readonly string _methodName;
    private long _sequence;
    private int _completed;
    private ProtoTraceOutcome _outcome = ProtoTraceOutcome.Unknown;
    private ProtoTraceError? _error;
    private TimeSpan _duration;
    private readonly ProtoTraceOptions _options;
    private readonly Action<ActivityTraceId, ProtoTestTraceRecorder>? _onTraceStarted;
    private readonly Action<ProtoTestTraceRecorder>? _onCompleted;
    private readonly ProtoItemStore _items = new();
    private readonly ProtoLock _orphanGate = new();
    private readonly ProtoLock _completionGate = new();
    private readonly List<ProtoTraceObservationRecord> _orphanObservations = [];
    private readonly List<ProtoTraceAttachmentRecord> _orphanAttachments = [];
    private readonly List<ProtoTraceFindingRecord> _orphanFindings = [];
    private readonly ConcurrentDictionary<string, EventGroup> _eventGroups = new(StringComparer.Ordinal);
    private string? _defaultParentId;
    private IReadOnlyList<ProtoTraceArtifactSource> _artifacts = [];

    public ProtoTestTraceRecorder(
        string testId,
        string name,
        MethodInfo method,
        ProtoTraceOptions? options = null,
        Action<ActivityTraceId, ProtoTestTraceRecorder>? onTraceStarted = null,
        Action<ProtoTestTraceRecorder>? onCompleted = null)
    {
        TestId = testId;
        Name = name;
        _className = method.DeclaringType?.FullName;
        _methodName = method.Name;
        _options = options ?? new ProtoTraceOptions();
        _onTraceStarted = onTraceStarted;
        _onCompleted = onCompleted;
    }

    public string TestId { get; }
    public string Name { get; }

    /// <summary>Whether this test completed; a completed recorder must not observe new spans.</summary>
    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

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
        Validate(kind, name, source);
        if (!_options.Enabled) return new ProtoTraceOperation();
        var id = NextId();
        var currentParent = _current.Value;
        var resolvedParentId = parentId ?? currentParent?.Id ?? Volatile.Read(ref _defaultParentId);
        var resolvedPhase = ResolvePhase(phase, resolvedParentId, currentParent);
        attributes = WithSourceLocation(attributes);
        var entry = new TraceEntryState(
            id,
            resolvedParentId,
            ProtoTraceEntryKind.Operation,
            kind,
            name,
            source,
            resolvedPhase,
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp(),
            attributes,
            currentParent,
            StartActivity(id, kind, name, source, resolvedPhase, attributes, resolvedParentId, entityKind, entityId),
            entityKind,
            entityId);
        _entries.Enqueue(entry);
        _entriesById.TryAdd(entry.Id, entry);
        _current.Value = entry;
        return new ProtoTraceOperation(this, entry);
    }

    private IReadOnlyDictionary<string, string?>? WithSourceLocation(IReadOnlyDictionary<string, string?>? attributes)
    {
        if (!_options.CaptureSourceLocations
            || attributes?.ContainsKey(ProtoSourceLocator.FilePathAttribute) == true
            || ProtoSourceLocator.Find() is not { } location)
        {
            return attributes;
        }

        var merged = attributes is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(attributes, StringComparer.Ordinal);
        merged[ProtoSourceLocator.FilePathAttribute] = location.File;
        merged[ProtoSourceLocator.LineNumberAttribute] = location.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        merged[ProtoSourceLocator.FunctionAttribute] = location.Function;
        return merged;
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
    {
        Validate(kind, name, source);
        if (!_options.Enabled) return;
        var currentParent = _current.Value;
        var resolvedParentId = parentId ?? currentParent?.Id ?? Volatile.Read(ref _defaultParentId);
        var resolvedPhase = ResolvePhase(phase, resolvedParentId, currentParent);

        // Identical error-free events under the same parent are one fact, not N rows: the recorder keeps
        // the first occurrence and counts the rest. Producers stay plain and third-party noise collapses too.
        // The event's name, source and resolved phase are part of the identity, so differently named events
        // - or the same event in a different phase - never merge.
        if (exception is null)
        {
            var group = _eventGroups.GetOrAdd(
                CoalesceKey(resolvedParentId, kind, name, source, resolvedPhase, entityKind, entityId, outcome, attributes),
                _ => new EventGroup(() => CreateEventEntry(
                    resolvedParentId, kind, name, source, resolvedPhase, attributes, outcome, entityKind, entityId)));
            group.Occur();
            return;
        }

        var entry = CreateEventEntry(
            resolvedParentId, kind, name, source, resolvedPhase, attributes, outcome, entityKind, entityId, exception);
    }

    private TraceEntryState CreateEventEntry(
        string? parentId,
        string kind,
        string name,
        string source,
        ProtoTracePhase phase,
        IReadOnlyDictionary<string, string?>? attributes,
        ProtoTraceOutcome outcome,
        string? entityKind,
        string? entityId,
        Exception? exception = null)
    {
        var entry = new TraceEntryState(
            NextId(),
            parentId,
            ProtoTraceEntryKind.Event,
            kind,
            name,
            source,
            phase,
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp(),
            attributes,
            parent: null,
            activity: null,
            entityKind,
            entityId);
        entry.Complete(outcome, exception);
        MarkFailedAncestors(entry, outcome);
        _entries.Enqueue(entry);
        _entriesById.TryAdd(entry.Id, entry);
        WriteActivityEvent(entry);
        return entry;
    }

    private static string CoalesceKey(
        string? parentId,
        string kind,
        string name,
        string source,
        ProtoTracePhase phase,
        string? entityKind,
        string? entityId,
        ProtoTraceOutcome outcome,
        IReadOnlyDictionary<string, string?>? attributes)
    {
        var builder = new StringBuilder();
        builder.Append(parentId).Append('\u001f').Append(kind).Append('\u001f')
            .Append(name).Append('\u001f').Append(source).Append('\u001f')
            .Append((int)phase).Append('\u001f')
            .Append(entityKind).Append('\u001f').Append(entityId).Append('\u001f').Append((int)outcome);
        if (attributes is { Count: > 0 })
        {
            foreach (var (key, value) in attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append('\u001f').Append(key).Append('=').Append(value);
            }
        }

        return builder.ToString();
    }

    public void SetEntityState(
        string kind,
        string id,
        string name,
        IReadOnlyDictionary<string, string?>? state = null,
        string? scope = null,
        string? change = null)
        => _items.SetState(
            kind, id, name, state, scope, change,
            _current.Value?.Id ?? Volatile.Read(ref _defaultParentId));

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
        Validate(kind, name, ProtoCoreDiagnostics.TraceSource);
        if (!_options.Enabled) return;
        var linkedOperationId = operationId
            ?? _current.Value?.Id
            ?? Volatile.Read(ref _defaultParentId);
        _items.AddValue(kind, id, name, change, linkedOperationId, state, source, inferred, scope);
    }

    public void Observation(
        string targetName,
        string kind,
        string identifier,
        string? data = null,
        string? metadata = null)
    {
        ProtoTraceRecords.ValidateObservation(targetName, kind, identifier);
        if (!_options.Enabled) return;
        var entry = _current.Value;
        if (entry is not null)
        {
            entry.AddObservation(targetName, kind, identifier, data, metadata);
        }
        else
        {
            lock (_orphanGate)
            {
                _orphanObservations.Add(ProtoTraceRecords.Observation(
                    string.Empty, null, targetName, kind, identifier, data, metadata));
            }
        }
        WriteRecordActivityEvent("observation", kind, targetName, identifier);
    }

    public void Attachment(string name, string mediaType, string? description = null)
    {
        ProtoTraceRecords.ValidateAttachment(name, mediaType);
        if (!_options.Enabled) return;
        var entry = _current.Value;
        if (entry is not null)
        {
            entry.AddAttachment(name, mediaType, description);
        }
        else
        {
            lock (_orphanGate)
            {
                _orphanAttachments.Add(ProtoTraceRecords.Attachment(string.Empty, null, name, mediaType, description));
            }
        }
        WriteRecordActivityEvent("attachment", mediaType, name, name);
    }

    public void Finding(
        string message,
        string status,
        string category,
        string? targetName = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ProtoTraceRecords.ValidateFinding(message, status, category);
        if (!_options.Enabled) return;
        // The recording boundary applies the evidence policy too, so a direct Trace.Finding caller
        // cannot put raw or cyclic metadata into the archive.
        metadata = ProtoMetadataRedaction.Redact(metadata);
        var entry = _current.Value;
        if (entry is not null)
        {
            entry.AddFinding(message, status, category, targetName, tags, metadata);
        }
        else
        {
            lock (_orphanGate)
            {
                _orphanFindings.Add(ProtoTraceRecords.Finding(
                    string.Empty, null, message, status, category, targetName, tags, metadata));
            }
        }
        WriteRecordActivityEvent("finding", category, message, status);
    }

    public string? CaptureActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!_options.Enabled) return null;
        var attributes = ProtoTraceActivity.Attributes(activity);
        var failed = ProtoTraceActivity.IsFailure(activity);
        var entry = new TraceEntryState(
            NextId(),
            _current.Value?.Id ?? Volatile.Read(ref _defaultParentId),
            ProtoTraceEntryKind.Operation,
            activity.Source.Name,
            activity.DisplayName,
            activity.Source.Name,
            ProtoTracePhase.Execution,
            activity.StartTimeUtc,
            Stopwatch.GetTimestamp(),
            attributes,
            parent: null,
            activity: null);
        entry.Complete(
            failed ? ProtoTraceOutcome.Failed : ProtoTraceOutcome.Succeeded,
            exception: null,
            error: ProtoTraceActivity.Error(activity),
            duration: activity.Duration);
        MarkFailedAncestors(entry, failed ? ProtoTraceOutcome.Failed : ProtoTraceOutcome.Succeeded);
        _entries.Enqueue(entry);
        _entriesById.TryAdd(entry.Id, entry);
        return entry.Id;
    }

    private ProtoTracePhase ResolvePhase(
        ProtoTracePhase requestedPhase,
        string? parentId,
        TraceEntryState? currentParent)
    {
        if (parentId is null) return requestedPhase;
        if (currentParent?.Id == parentId) return currentParent.Phase;
        return _entriesById.TryGetValue(parentId, out var parent) ? parent.Phase : requestedPhase;
    }

    public void CompleteTest(ProtoTestResult result)
    {
        // The duration, outcome and error are published before the completed flag: a snapshot that sees
        // the flag set must see the final fields, not a zero duration and an unknown outcome.
        lock (_completionGate)
        {
            if (_completed != 0) return;
            _duration = Stopwatch.GetElapsedTime(_startedTimestamp);
            _outcome = result.Outcome == ProtoTraceOutcome.Succeeded && _entries.Any(entry =>
                    entry.Outcome is ProtoTraceOutcome.Failed or ProtoTraceOutcome.Partial)
                ? ProtoTraceOutcome.Partial
                : result.Outcome;
            _error = result.Error ?? (result.Exception is null ? null : ProtoTraceError.FromException(result.Exception));
            Volatile.Write(ref _completed, 1);
        }

        foreach (var entry in _entries)
        {
            entry.CompleteIfOpen(ProtoTraceOutcome.Unknown);
        }
        _current.Value = null;

        // The converter's per-writer state exists only while the writer can produce values; a completed
        // recorder never observes another span, so releasing it stops the map growing with every run.
        _onCompleted?.Invoke(this);
    }

    internal async Task CaptureArtifactsAsync(
        IReadOnlyList<ProtoTestAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ProtoTraceArtifactSource>(attachments.Count);
        // The snapshot list is published before any record is patched: an attachment record must never
        // point at an artifact that a later capture failure would leave out of the snapshot.
        _artifacts = artifacts;
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            var id = $"artifact-{index + 1}";
            var archivePath = $"resources/{ProtoPathSanitizer.Segment(TestId, "artifact")}/{id}/{ProtoPathSanitizer.Segment(attachment.Name, "artifact")}";
            var artifact = new ProtoTraceArtifact(
                id,
                attachment.Name,
                attachment.MediaType,
                attachment.Description,
                archivePath);
            var source = await ProtoArtifactCapture.CaptureAsync(
                artifact,
                attachment,
                _options.MaxArtifactBytes,
                _options.EmbedArtifacts,
                cancellationToken);
            artifacts.Add(source);
            PatchAttachment(attachment.Name, id, archivePath, source.Content.Length, source.Artifact.Error);
        }
    }

    internal IReadOnlyList<ProtoTraceArtifactSource> SnapshotArtifactSources() => _artifacts;

    /// <summary>
    /// The record axis is events on the spans that produced them: each entry carries its observations,
    /// attachments and findings, and items recorded outside an operation are kept as orphans.
    /// </summary>
    private ProtoTraceRecord? SnapshotRecord()
    {
        List<ProtoTraceObservationRecord> observations = [];
        List<ProtoTraceAttachmentRecord> attachments = [];
        List<ProtoTraceFindingRecord> findings = [];
        foreach (var entry in _entries)
        {
            entry.CollectRecord(observations, attachments, findings);
        }

        lock (_orphanGate)
        {
            observations.AddRange(_orphanObservations);
            attachments.AddRange(_orphanAttachments);
            findings.AddRange(_orphanFindings);
        }

        if (observations.Count == 0 && attachments.Count == 0 && findings.Count == 0)
        {
            return null;
        }

        observations.Sort((left, right) => left.AtUtc.CompareTo(right.AtUtc));
        attachments.Sort((left, right) => left.AtUtc.CompareTo(right.AtUtc));
        findings.Sort((left, right) => left.AtUtc.CompareTo(right.AtUtc));
        for (var index = 0; index < observations.Count; index++)
        {
            observations[index] = observations[index] with { Id = $"observation-{index + 1}" };
        }

        for (var index = 0; index < attachments.Count; index++)
        {
            attachments[index] = attachments[index] with { Id = $"attachment-{index + 1}" };
        }

        for (var index = 0; index < findings.Count; index++)
        {
            findings[index] = findings[index] with { Id = $"finding-{index + 1}" };
        }

        return new ProtoTraceRecord(
            observations.Count == 0 ? null : observations,
            attachments.Count == 0 ? null : attachments,
            findings.Count == 0 ? null : findings);
    }

    private void PatchAttachment(string name, string artifactId, string? archivePath, long sizeBytes, string? error)
    {
        foreach (var entry in _entries.Reverse())
        {
            if (entry.TryPatchAttachment(name, artifactId, archivePath, sizeBytes, error))
            {
                return;
            }
        }

        lock (_orphanGate)
        {
            var index = _orphanAttachments.FindLastIndex(attachment =>
                string.Equals(attachment.Name, name, StringComparison.Ordinal)
                && attachment.ArtifactId is null);
            if (index >= 0)
            {
                _orphanAttachments[index] = _orphanAttachments[index] with
                {
                    ArtifactId = artifactId,
                    ArchivePath = archivePath,
                    SizeBytes = sizeBytes,
                    Error = error
                };
            }
        }
    }

    internal void SetDefaultParent(string? operationId)
        => Volatile.Write(ref _defaultParentId, operationId);

    internal void Complete(TraceEntryState entry, ProtoTraceOutcome outcome, Exception? exception)
    {
        var resolved = ResolveOutcome(entry, outcome);
        entry.Complete(resolved, exception);
        MarkFailedAncestors(entry, resolved);
        if (ReferenceEquals(_current.Value, entry))
        {
            _current.Value = entry.Parent;
        }
    }

    internal void Complete(TraceEntryState entry, ProtoTestResult result)
    {
        var resolved = ResolveOutcome(entry, result.Outcome);
        entry.Complete(resolved, result.Exception, result.Error);
        MarkFailedAncestors(entry, resolved);
        if (ReferenceEquals(_current.Value, entry))
        {
            _current.Value = entry.Parent;
        }
    }

    private static ProtoTraceOutcome ResolveOutcome(TraceEntryState entry, ProtoTraceOutcome outcome)
        => outcome == ProtoTraceOutcome.Succeeded && entry.HasFailedDescendant
            ? ProtoTraceOutcome.Partial
            : outcome;

    /// <summary>
    /// Marks every ancestor of a failed entry, so an operation that succeeds while a child failed
    /// resolves to partial without scanning the entries recorded after it.
    /// </summary>
    private void MarkFailedAncestors(TraceEntryState entry, ProtoTraceOutcome outcome)
    {
        if (outcome is not (ProtoTraceOutcome.Failed or ProtoTraceOutcome.Partial))
        {
            return;
        }

        for (var parent = ParentOf(entry); parent is not null; parent = ParentOf(parent))
        {
            parent.MarkFailedDescendant();
        }
    }

    private TraceEntryState? ParentOf(TraceEntryState entry)
        => entry.Parent
           ?? (entry.ParentId is not null && _entriesById.TryGetValue(entry.ParentId, out var parent) ? parent : null);

    public ProtoTestTrace Snapshot()
    {
        var duration = Volatile.Read(ref _completed) == 0
            ? Stopwatch.GetElapsedTime(_startedTimestamp)
            : _duration;
        return new ProtoTestTrace(
            TestId,
            Name,
            _className,
            _methodName,
            _startedAtUtc,
            duration,
            _outcome,
            _error,
            _entries.Select(entry => entry.Snapshot()).ToArray(),
            _artifacts.Select(source => source.Artifact).ToArray(),
            _items.SnapshotEntities(),
            _items.SnapshotValues(),
            SnapshotRecord());
    }

    private string NextId() => Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);

    private Activity? StartActivity(
        string entryId,
        string kind,
        string name,
        string source,
        ProtoTracePhase phase,
        IReadOnlyDictionary<string, string?>? attributes,
        string? logicalParentId,
        string? entityKind,
        string? entityId)
    {
        // The logical parent's Activity may not be Activity.Current: the test's execution operation is started
        // inside StartTestAsync, whose ambient Activity does not flow back out to the test body.
        var parent = ParentActivity(logicalParentId);
        var activity = parent is null || ReferenceEquals(Activity.Current, parent)
            ? ProtoTestDiagnostics.ActivitySource.StartActivity(name, ActivityKind.Internal)
            : ProtoTestDiagnostics.ActivitySource.StartActivity(name, ActivityKind.Internal, parent.Context);
        if (activity is null) return null;
        if (parent is null)
        {
            // A root activity opens this test's trace context: spans from the application that carry
            // this trace id are the test's own execution, wherever they were produced.
            _onTraceStarted?.Invoke(activity.TraceId, this);
        }

        activity.SetTag("prototest.test.id", TestId);
        activity.SetTag("prototest.entry.id", entryId);
        activity.SetTag("prototest.entry.kind", kind);
        activity.SetTag("prototest.source", source);
        activity.SetTag("prototest.phase", phase.ToString().ToLowerInvariant());
        if (logicalParentId is not null) activity.SetTag("prototest.logical_parent_id", logicalParentId);
        if (entityKind is not null) activity.SetTag("prototest.entity.kind", entityKind);
        if (entityId is not null) activity.SetTag("prototest.entity.id", entityId);
        AddActivityTags(activity, attributes);
        return activity;
    }

    private Activity? ParentActivity(string? parentId)
        => parentId is not null && _entriesById.TryGetValue(parentId, out var parent) ? parent.Activity : null;

    private void WriteRecordActivityEvent(string recordKind, string kind, string name, string identifier)
    {
        var activity = _current.Value?.Activity ?? Activity.Current;
        if (activity is null || activity.IsStopped)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            ["prototest.record.kind"] = recordKind,
            ["prototest.record.type"] = kind,
            ["prototest.record.name"] = name,
            ["prototest.record.identifier"] = identifier
        };
        activity.AddEvent(new ActivityEvent(name, DateTimeOffset.UtcNow, tags));
    }

    private void WriteActivityEvent(TraceEntryState entry)
    {
        var activity = ParentActivity(entry.ParentId) ?? Activity.Current;
        if (activity is null) return;
        var tags = new ActivityTagsCollection
        {
            ["prototest.entry.kind"] = entry.Kind,
            ["prototest.entry.id"] = entry.Id,
            ["prototest.source"] = entry.Source,
            ["prototest.phase"] = entry.Phase.ToString().ToLowerInvariant(),
            ["prototest.outcome"] = entry.Outcome.ToString().ToLowerInvariant()
        };
        foreach (var (key, value) in entry.Attributes)
            if (ShouldExportTag(key, value)) tags[key] = value;
        activity.AddEvent(new ActivityEvent(entry.Name, entry.TimestampUtc, tags));
    }

    private static void AddActivityTags(Activity activity, IReadOnlyDictionary<string, string?>? attributes)
    {
        if (attributes is null) return;
        foreach (var (key, value) in attributes)
            if (ShouldExportTag(key, value)) activity.SetTag(key, value);
    }

    /// <summary>The longest tag value kept in the trace or exported; a longer value is dropped by both.</summary>
    internal const int MaxTagValueLength = 2048;

    internal static bool ShouldExportTag(string key, string? value)
        => value is not null
           && value.Length <= MaxTagValueLength
           && key is not "context.value" and not "observation.data" and not "observation.metadata"
           && key is not "shape.expected" and not "shape.actual" and not "shape.matches" and not "shape.mismatches";

    private static void Validate(string kind, string name, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }

    /// <summary>One recorded fact; repeats increment its count instead of adding a row.</summary>
    private sealed class EventGroup
    {
        private readonly ProtoLock _gate = new();
        private readonly Lazy<TraceEntryState> _entry;
        private int _occurrences;

        public EventGroup(Func<TraceEntryState> create)
            => _entry = new Lazy<TraceEntryState>(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public void Occur()
        {
            lock (_gate)
            {
                _occurrences++;
                _entry.Value.SetCount(_occurrences);
            }
        }
    }

}

