namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

internal sealed class ProtoTraceSession : IProtoTraceSource
{
    internal const string CurrentFormatVersion = "1.2";
    private readonly ConcurrentDictionary<string, ProtoTestTraceRecorder> _tests = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ProtoTraceArtifactSource> _runArtifacts = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private DateTimeOffset? _completedAtUtc;
    private readonly ProtoTraceOptions _options;

    public ProtoTraceSession(ProtoTraceOptions? options = null)
    {
        _options = options ?? new ProtoTraceOptions();
    }

    public ProtoTestTraceRecorder StartTest(string name, ProtoTestId testId, MethodInfo method)
    {
        var recorder = new ProtoTestTraceRecorder(testId.Value, name, method, _options);
        if (!_tests.TryAdd(testId.Value, recorder))
        {
            throw new InvalidOperationException($"A trace already exists for test ID '{testId.Value}'.");
        }
        return recorder;
    }

    public void CompleteRun() => _completedAtUtc ??= DateTimeOffset.UtcNow;

    public ProtoTraceRun Snapshot()
        => new(
            CurrentFormatVersion,
            _runId,
            _startedAtUtc,
            _completedAtUtc,
            _tests.Values
                .Select(test => test.Snapshot())
                .OrderBy(test => test.StartedAtUtc)
                .ThenBy(test => test.TestId, StringComparer.Ordinal)
                .ToArray(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = RuntimeInformation.OSDescription,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString()
            },
            _runArtifacts.Select(source => source.Artifact).ToArray());

    internal async Task CaptureRunArtifactsAsync(
        IReadOnlyCollection<ProtoTestAttachment> attachments,
        string sourceName,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in attachments)
        {
            var sequence = _runArtifacts.Count + 1;
            var id = $"run-artifact-{sequence}";
            var archivePath = $"resources/run/{SanitizePathSegment(sourceName)}/{id}/{SanitizePathSegment(attachment.Name)}";
            var artifact = new ProtoTraceArtifact(id, attachment.Name, attachment.MediaType, attachment.Description, archivePath);
            ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
            try
            {
                content = await attachment.ReadAllBytesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                artifact = artifact with { Error = exception.Message };
            }
            _runArtifacts.Enqueue(new ProtoTraceArtifactSource(artifact, content));
        }
    }

    internal IReadOnlyList<ProtoTraceArtifactSource> SnapshotArtifactSources()
        => _runArtifacts
            .Concat(_tests.Values.SelectMany(test => test.SnapshotArtifactSources()))
            .ToArray();

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. value.Select(character => invalid.Contains(character) ? '_' : character)]);
        return string.IsNullOrWhiteSpace(sanitized) ? "artifact" : sanitized;
    }
}

internal sealed record ProtoTraceArtifactSource(ProtoTraceArtifact Artifact, ReadOnlyMemory<byte> Content);

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
    private string? _defaultParentId;
    private IReadOnlyList<ProtoTraceArtifactSource> _artifacts = [];

    public ProtoTestTraceRecorder(
        string testId,
        string name,
        MethodInfo method,
        ProtoTraceOptions? options = null)
    {
        TestId = testId;
        Name = name;
        _className = method.DeclaringType?.FullName;
        _methodName = method.Name;
        _options = options ?? new ProtoTraceOptions();
    }

    public string TestId { get; }
    public string Name { get; }

    public ProtoTraceOperation StartOperation(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
    {
        Validate(kind, name, source);
        if (!_options.Enabled) return new ProtoTraceOperation();
        var id = NextId();
        var currentParent = _current.Value;
        var resolvedParentId = parentId ?? currentParent?.Id ?? Volatile.Read(ref _defaultParentId);
        var resolvedPhase = ResolvePhase(phase, resolvedParentId, currentParent);
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
            StartActivity(id, kind, name, source, resolvedPhase, attributes, resolvedParentId));
        _entries.Enqueue(entry);
        _entriesById.TryAdd(entry.Id, entry);
        _current.Value = entry;
        return new ProtoTraceOperation(this, entry);
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
    {
        Validate(kind, name, source);
        if (!_options.Enabled) return;
        var currentParent = _current.Value;
        var resolvedParentId = parentId ?? currentParent?.Id ?? Volatile.Read(ref _defaultParentId);
        var resolvedPhase = ResolvePhase(phase, resolvedParentId, currentParent);
        var entry = new TraceEntryState(
            NextId(),
            resolvedParentId,
            ProtoTraceEntryKind.Event,
            kind,
            name,
            source,
            resolvedPhase,
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp(),
            attributes,
            parent: null);
        entry.Complete(outcome, exception);
        _entries.Enqueue(entry);
        _entriesById.TryAdd(entry.Id, entry);
        WriteActivityEvent(entry);
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
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        _duration = Stopwatch.GetElapsedTime(_startedTimestamp);
        _outcome = result.Outcome == ProtoTraceOutcome.Succeeded && _entries.Any(entry =>
                entry.Outcome is ProtoTraceOutcome.Failed or ProtoTraceOutcome.Partial)
            ? ProtoTraceOutcome.Partial
            : result.Outcome;
        _error = result.Error ?? (result.Exception is null ? null : ProtoTraceError.FromException(result.Exception));

        foreach (var entry in _entries)
        {
            entry.CompleteIfOpen(ProtoTraceOutcome.Unknown);
        }
        _current.Value = null;
    }

    internal async Task CaptureArtifactsAsync(
        IReadOnlyList<ProtoTestAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ProtoTraceArtifactSource>(attachments.Count);
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            var id = $"artifact-{index + 1}";
            var archivePath = $"resources/{SanitizePathSegment(TestId)}/{id}/{SanitizePathSegment(attachment.Name)}";
            var artifact = new ProtoTraceArtifact(
                id,
                attachment.Name,
                attachment.MediaType,
                attachment.Description,
                archivePath);
            ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
            try
            {
                content = await attachment.ReadAllBytesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                artifact = artifact with { Error = exception.Message };
            }

            artifacts.Add(new ProtoTraceArtifactSource(artifact, content));
            foreach (var entry in _entries.Where(entry =>
                         entry.Kind.StartsWith("attachment.", StringComparison.Ordinal) &&
                         string.Equals(
                             entry.Attributes.GetValueOrDefault("attachment.name"),
                             attachment.Name,
                             StringComparison.Ordinal)))
            {
                entry.SetAttribute("attachment.artifact_id", id);
                entry.SetAttribute("attachment.archive_path", archivePath);
                entry.SetAttribute("attachment.size_bytes", content.Length.ToString(CultureInfo.InvariantCulture));
                if (artifact.Error is not null) entry.SetAttribute("attachment.error", artifact.Error);
            }
        }

        _artifacts = artifacts;
    }

    internal IReadOnlyList<ProtoTraceArtifactSource> SnapshotArtifactSources() => _artifacts;

    internal void SetDefaultParent(string? operationId)
        => Volatile.Write(ref _defaultParentId, operationId);

    internal void Complete(TraceEntryState entry, ProtoTraceOutcome outcome, Exception? exception)
    {
        entry.Complete(ResolveOutcome(entry, outcome), exception);
        if (ReferenceEquals(_current.Value, entry))
        {
            _current.Value = entry.Parent;
        }
    }

    internal void Complete(TraceEntryState entry, ProtoTestResult result)
    {
        entry.Complete(ResolveOutcome(entry, result.Outcome), result.Exception, result.Error);
        if (ReferenceEquals(_current.Value, entry))
        {
            _current.Value = entry.Parent;
        }
    }

    private ProtoTraceOutcome ResolveOutcome(TraceEntryState entry, ProtoTraceOutcome outcome)
        => outcome == ProtoTraceOutcome.Succeeded && _entries.Any(candidate =>
            candidate.ParentId == entry.Id && candidate.Outcome is ProtoTraceOutcome.Failed or ProtoTraceOutcome.Partial)
            ? ProtoTraceOutcome.Partial
            : outcome;

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
            _artifacts.Select(source => source.Artifact).ToArray());
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "artifact" : sanitized;
    }

    private string NextId() => Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);

    private Activity? StartActivity(
        string entryId,
        string kind,
        string name,
        string source,
        ProtoTracePhase phase,
        IReadOnlyDictionary<string, string?>? attributes,
        string? logicalParentId)
    {
        var activity = ProtoTestDiagnostics.ActivitySource.StartActivity(name, ActivityKind.Internal);
        if (activity is null) return null;
        activity.SetTag("prototest.test.id", TestId);
        activity.SetTag("prototest.entry.id", entryId);
        activity.SetTag("prototest.entry.kind", kind);
        activity.SetTag("prototest.source", source);
        activity.SetTag("prototest.phase", phase.ToString().ToLowerInvariant());
        if (logicalParentId is not null) activity.SetTag("prototest.logical_parent_id", logicalParentId);
        AddActivityTags(activity, attributes);
        return activity;
    }

    private static void WriteActivityEvent(TraceEntryState entry)
    {
        var activity = Activity.Current;
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

    private static bool ShouldExportTag(string key, string? value)
        => value is not null
           && value.Length <= 2048
           && key is not "context.value" and not "observation.data" and not "observation.metadata"
           && key is not "shape.expected" and not "shape.actual" and not "shape.matches" and not "shape.mismatches";

    private static void Validate(string kind, string name, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }

    internal sealed class TraceEntryState
    {
        private readonly object _gate = new();
        private readonly long _startedTimestamp;
        private readonly Dictionary<string, string?> _attributes;
        private int _completed;
        private TimeSpan? _duration;
        private ProtoTraceOutcome _outcome;
        private ProtoTraceError? _error;
        private readonly Activity? _activity;

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
            Activity? activity = null)
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
        public ProtoTraceOutcome Outcome { get { lock (_gate) return _outcome; } }
        public IReadOnlyDictionary<string, string?> Attributes { get { lock (_gate) return new Dictionary<string, string?>(_attributes); } }

        public void SetAttribute(string name, string? value)
        {
            lock (_gate) _attributes[name] = value;
        }

        public void Complete(
            ProtoTraceOutcome outcome,
            Exception? exception,
            ProtoTraceError? error = null)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            lock (_gate)
            {
                _outcome = outcome;
                _error = error ?? (exception is null ? null : ProtoTraceError.FromException(exception));
                if (EntryKind == ProtoTraceEntryKind.Operation)
                    _duration = Stopwatch.GetElapsedTime(_startedTimestamp);
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
                if (ShouldExportTag(key, value)) _activity.SetTag(key, value);
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
                    _error);
            }
        }
    }
}
