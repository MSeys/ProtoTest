namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class ProtoTraceSession : IProtoTraceSource
{
    internal const string CurrentFormatVersion = "1.9";
    private readonly ConcurrentDictionary<string, ProtoTestTraceRecorder> _tests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProtoTestTraceRecorder> _testsByTraceId = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ProtoTraceArtifactSource> _runArtifacts = new();
    private readonly ProtoRunTraceWriter _runWriter = new();
    private readonly ProtoSpanConverter _converter = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private DateTimeOffset? _completedAtUtc;
    private readonly ProtoTraceOptions _options;
    private ActivityListener? _activityListener;
    private int _listening;

    public ProtoTraceSession(ProtoTraceOptions? options = null)
    {
        _options = options ?? new ProtoTraceOptions();
    }

    /// <summary>
    /// Starts capturing spans from the watched activity sources. Application instrumentation is ordinary
    /// OpenTelemetry: ProtoTest listens, it does not ask the application to know about ProtoTest.
    /// </summary>
    public void StartListening()
    {
        if (_options.ActivitySources.Count == 0 || Interlocked.Exchange(ref _listening, 1) != 0)
        {
            return;
        }

        var listener = new ActivityListener
        {
            // ProtoTest's own activities are not captured through the listener (their writer already
            // records them semantically); sampling them keeps the W3C trace context alive so application
            // spans can be linked back to the test that caused them.
            ShouldListenTo = source => source.Name == ProtoTestDiagnostics.ActivitySourceName
                || _options.ActivitySources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };
        ActivitySource.AddActivityListener(listener);
        _activityListener = listener;
    }

    public void StopListening()
    {
        _activityListener?.Dispose();
        _activityListener = null;
        Interlocked.Exchange(ref _listening, 0);
    }

    private void OnActivityStopped(Activity activity)
    {
        if (activity.Source.Name == ProtoTestDiagnostics.ActivitySourceName)
        {
            return;
        }

        try
        {
            // A span that carries a test's trace id belongs to that test even though the callback runs
            // outside its flow - the whole point of propagating context across the app boundary.
            var writer = FindWriter(activity.TraceId)
                ?? (IProtoTraceWriter?)ProtoHost.CurrentContextOrNull?.Trace
                ?? _runWriter;
            var operationId = writer.CaptureActivity(activity);
            _converter.Observe(writer, activity, operationId);
        }
        catch
        {
            // Capturing telemetry must never break the application.
        }
    }

    /// <inheritdoc />
    public IProtoTraceWriter? FindWriter(ActivityTraceId traceId)
        => _testsByTraceId.TryGetValue(traceId.ToHexString(), out var recorder) ? recorder : null;

    private void RegisterTrace(ActivityTraceId traceId, ProtoTestTraceRecorder recorder)
        => _testsByTraceId.TryAdd(traceId.ToHexString(), recorder);

    public ProtoTestTraceRecorder StartTest(string name, ProtoTestId testId, MethodInfo method)
    {
        var recorder = new ProtoTestTraceRecorder(testId.Value, name, method, _options, RegisterTrace);
        if (!_tests.TryAdd(testId.Value, recorder))
        {
            throw new InvalidOperationException($"A trace already exists for test ID '{testId.Value}'.");
        }
        return recorder;
    }

    /// <summary>Gets the writer for operations that belong to the run rather than to one test.</summary>
    public IProtoTraceWriter RunWriter => _runWriter;

    public void CompleteRun() => _completedAtUtc ??= DateTimeOffset.UtcNow;

    public ProtoTraceRun Snapshot()
    {
        var tests = _tests.Values
            .Select(test => test.Snapshot())
            .OrderBy(test => test.StartedAtUtc)
            .ThenBy(test => test.TestId, StringComparer.Ordinal)
            .ToArray();
        var entities = _runWriter.SnapshotEntities();
        var values = _runWriter.SnapshotValues();

        return new ProtoTraceRun(
            CurrentFormatVersion,
            _runId,
            _startedAtUtc,
            _completedAtUtc,
            tests,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = RuntimeInformation.OSDescription,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString()
            },
            _runArtifacts.Select(source => source.Artifact).ToArray(),
            _runWriter.Snapshot(),
            entities,
            values,
            DeriveVisibility(tests, entities, values),
            _runWriter.SnapshotRecord());
    }

    /// <summary>
    /// Visibility is derived from what the trace already contains, so it cannot drift from reality:
    /// capabilities say what was composed, server and client entities say where the application ran, and
    /// the value sources say how deep the integration reached.
    /// </summary>
    private static ProtoTraceVisibility DeriveVisibility(
        IReadOnlyList<ProtoTestTrace> tests,
        IReadOnlyList<ProtoTraceEntity> runEntities,
        IReadOnlyList<ProtoTraceValue> runValues)
    {
        var capabilities = runEntities.Where(entity => entity.Kind == ProtoTraceEntityKinds.Capability).ToArray();
        var testEntities = tests.SelectMany(test => test.Entities ?? []).ToArray();
        var hasServer = capabilities.Any(capability => CapabilityKind(capability) == ProtoCapabilityKinds.Server)
            || testEntities.Any(entity => entity.Kind == ProtoTraceEntityKinds.Server);
        var hasClients = testEntities.Any(entity => entity.Kind == ProtoTraceEntityKinds.Client);
        var hosting = hasServer ? "in-process" : hasClients ? "remote" : "unknown";

        var backends = capabilities
            .Where(capability => CapabilityKind(capability) is
                ProtoCapabilityKinds.Server or ProtoCapabilityKinds.Store or
                ProtoCapabilityKinds.Broker or ProtoCapabilityKinds.Data)
            .Select(capability => capability.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var versions = tests.SelectMany(test => test.Values ?? [])
            .Concat(runValues)
            .SelectMany(value => value.Versions)
            .ToArray();
        var sources = new List<string>();
        if (versions.Any(version => version.Source == ProtoTraceValueSource.TestSide)) sources.Add("test-side");
        if (versions.Any(version => version.Source == ProtoTraceValueSource.Observed)) sources.Add("observed");
        var applicationInstrumented = versions.Any(version => version.Source == ProtoTraceValueSource.ApplicationSide);
        if (applicationInstrumented) sources.Add("application-side");

        return new ProtoTraceVisibility(hosting, backends, sources, applicationInstrumented);
    }

    private static string? CapabilityKind(ProtoTraceEntity capability)
        => capability.State.TryGetValue("capability.kind", out var kind) ? kind : null;

    internal async Task CaptureRunArtifactsAsync(
        IReadOnlyCollection<ProtoTestAttachment> attachments,
        string sourceName,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in attachments)
        {
            var sequence = _runArtifacts.Count + 1;
            var id = $"run-artifact-{sequence}";
            var archivePath = $"resources/run/{ProtoPathSanitizer.FileName(sourceName, "artifact")}/{id}/{ProtoPathSanitizer.FileName(attachment.Name, "artifact")}";
            var artifact = new ProtoTraceArtifact(id, attachment.Name, attachment.MediaType, attachment.Description, archivePath);
            ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
            try
            {
                content = await attachment.ReadAllBytesAsync(cancellationToken);
                artifact = artifact with { SizeBytes = content.Length };
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
    private readonly Action<ActivityTraceId, ProtoTestTraceRecorder>? _onTraceStarted;
    private readonly ProtoItemStore _items = new();
    private readonly ProtoLock _orphanGate = new();
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
        Action<ActivityTraceId, ProtoTestTraceRecorder>? onTraceStarted = null)
    {
        TestId = testId;
        Name = name;
        _className = method.DeclaringType?.FullName;
        _methodName = method.Name;
        _options = options ?? new ProtoTraceOptions();
        _onTraceStarted = onTraceStarted;
    }

    public string TestId { get; }
    public string Name { get; }

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
        if (exception is null)
        {
            var group = _eventGroups.GetOrAdd(
                CoalesceKey(resolvedParentId, kind, entityKind, entityId, outcome, attributes),
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
        _entries.Enqueue(entry);
        _entriesById.TryAdd(entry.Id, entry);
        WriteActivityEvent(entry);
        return entry;
    }

    private static string CoalesceKey(
        string? parentId,
        string kind,
        string? entityKind,
        string? entityId,
        ProtoTraceOutcome outcome,
        IReadOnlyDictionary<string, string?>? attributes)
    {
        var builder = new StringBuilder();
        builder.Append(parentId).Append('\u001f').Append(kind).Append('\u001f')
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
        => _items.SetState(kind, id, name, state, scope, change, _current.Value?.Id);

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
        Validate(kind, name, "ProtoTest.Core");
        if (!_options.Enabled) return;
        var operation = _current.Value;
        _items.AddValue(kind, id, name, change, operationId ?? operation?.Id, state, source, inferred, scope);
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
        if (!_options.Enabled) return;
        var entry = _current.Value;
        if (entry is not null)
        {
            entry.AddObservation(targetName, kind, identifier, data, metadata);
        }
        else
        {
            _orphanObservations.Add(new ProtoTraceObservationRecord(
                string.Empty,
                null,
                DateTimeOffset.UtcNow,
                targetName,
                kind,
                identifier,
                data,
                metadata));
        }
        WriteRecordActivityEvent("observation", kind, targetName, identifier);
    }

    public void Attachment(string name, string mediaType, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (!_options.Enabled) return;
        var entry = _current.Value;
        if (entry is not null)
        {
            entry.AddAttachment(name, mediaType, description);
        }
        else
        {
            _orphanAttachments.Add(new ProtoTraceAttachmentRecord(
                string.Empty,
                null,
                DateTimeOffset.UtcNow,
                name,
                mediaType,
                description));
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
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (!_options.Enabled) return;
        var entry = _current.Value;
        if (entry is not null)
        {
            entry.AddFinding(message, status, category, targetName, tags, metadata);
        }
        else
        {
            _orphanFindings.Add(new ProtoTraceFindingRecord(
                string.Empty,
                null,
                DateTimeOffset.UtcNow,
                message,
                status,
                category,
                targetName,
                tags,
                metadata));
        }
        WriteRecordActivityEvent("finding", category, message, status);
    }

    public string? CaptureActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!_options.Enabled) return null;
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["activity.source"] = activity.Source.Name
        };
        foreach (var tag in activity.TagObjects)
        {
            attributes[tag.Key] = tag.Value?.ToString();
        }

        var failed = activity.Status == ActivityStatusCode.Error;
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
            error: failed && activity.StatusDescription is { Length: > 0 } description
                ? new ProtoTraceError("ActivityError", description)
                : null,
            duration: activity.Duration);
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
            var archivePath = $"resources/{ProtoPathSanitizer.Segment(TestId, "artifact")}/{id}/{ProtoPathSanitizer.Segment(attachment.Name, "artifact")}";
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
                artifact = artifact with { SizeBytes = content.Length };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                artifact = artifact with { Error = exception.Message };
            }

            artifacts.Add(new ProtoTraceArtifactSource(artifact, content));
            PatchAttachment(attachment.Name, id, archivePath, content.Length, artifact.Error);
        }

        _artifacts = artifacts;
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
        public IReadOnlyDictionary<string, string?> Attributes { get { lock (_gate) return new Dictionary<string, string?>(_attributes); } }

        public void SetAttribute(string name, string? value)
        {
            lock (_gate) _attributes[name] = value;
        }

        public void SetCount(int count)
        {
            lock (_gate)
            {
                _count = count;
                if (_activity is { IsStopped: false })
                {
                    _activity.SetTag("prototest.entry.count", count);
                }
            }
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
            lock (_gate)
            {
                _recordObservations.Add(new ProtoTraceObservationRecord(
                    string.Empty,
                    Id,
                    DateTimeOffset.UtcNow,
                    targetName,
                    kind,
                    identifier,
                    data,
                    metadata));
            }
        }

        public void AddAttachment(string name, string mediaType, string? description)
        {
            lock (_gate)
            {
                _recordAttachments.Add(new ProtoTraceAttachmentRecord(
                    string.Empty,
                    Id,
                    DateTimeOffset.UtcNow,
                    name,
                    mediaType,
                    description));
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
            lock (_gate)
            {
                _recordFindings.Add(new ProtoTraceFindingRecord(
                    string.Empty,
                    Id,
                    DateTimeOffset.UtcNow,
                    message,
                    status,
                    category,
                    targetName,
                    tags,
                    metadata));
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
                    _error,
                    EntityKind,
                    EntityId,
                    _count,
                    _sections.Count == 0 ? null : [.. _sections]);
            }
        }
    }
}
