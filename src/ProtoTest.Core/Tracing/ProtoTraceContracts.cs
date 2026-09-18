namespace ProtoTest.Core;

using System.Diagnostics;

public enum ProtoTracePhase
{
    Run,
    Setup,
    Execution,
    Rollback,
    Teardown
}

public enum ProtoTraceOutcome
{
    Unknown,
    Succeeded,
    Partial,
    Failed,
    Cancelled,
    Skipped
}

public enum ProtoTraceEntryKind
{
    Operation,
    Event
}

public sealed record ProtoTraceError(
    string Type,
    string Message,
    string? StackTrace = null)
{
    internal static ProtoTraceError FromException(Exception exception)
        => new(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace);
}

public sealed record ProtoTraceEntry(
    string Id,
    string? ParentId,
    ProtoTraceEntryKind EntryKind,
    string Kind,
    string Name,
    string Source,
    ProtoTracePhase Phase,
    DateTimeOffset TimestampUtc,
    TimeSpan? Duration,
    ProtoTraceOutcome Outcome,
    IReadOnlyDictionary<string, string?> Attributes,
    ProtoTraceError? Error = null,
    string? EntityKind = null,
    string? EntityId = null,
    int Count = 1,
    IReadOnlyList<ProtoTraceSection>? Sections = null);

public sealed record ProtoTraceArtifact(
    string Id,
    string Name,
    string MediaType,
    string? Description,
    string ArchivePath,
    string? Error = null,
    long? SizeBytes = null);

/// <summary>
/// The current state of one thing the run composed: a client, context, resource, server or capability.
/// Entities are state, not history: an entity appears once in the artifact with its latest state and its
/// versions, and entries refer to it by <see cref="ProtoTraceEntry.EntityId"/>.
/// </summary>
public sealed record ProtoTraceEntity(
    string Id,
    string Kind,
    string Name,
    string? Scope,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    IReadOnlyDictionary<string, string?> State,
    IReadOnlyList<ProtoTraceVersion>? Versions = null);

/// <summary>The entity kinds ProtoTest itself records; an integration may define its own.</summary>
public static class ProtoTraceEntityKinds
{
    public const string Client = "client";
    public const string Context = "context";
    public const string Auth = "auth";
    public const string Server = "server";
    public const string Capability = "capability";
}

public sealed record ProtoTestTrace(
    string TestId,
    string Name,
    string? ClassName,
    string MethodName,
    DateTimeOffset StartedAtUtc,
    TimeSpan Duration,
    ProtoTraceOutcome Outcome,
    ProtoTraceError? Error,
    IReadOnlyList<ProtoTraceEntry> Entries,
    IReadOnlyList<ProtoTraceArtifact> Artifacts,
    IReadOnlyList<ProtoTraceEntity>? Entities = null,
    IReadOnlyList<ProtoTraceValue>? Values = null,
    ProtoTraceRecord? Record = null);

public sealed record ProtoTraceRun(
    string FormatVersion,
    string RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<ProtoTestTrace> Tests,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<ProtoTraceArtifact>? Artifacts = null,
    IReadOnlyList<ProtoTraceEntry>? Entries = null,
    IReadOnlyList<ProtoTraceEntity>? Entities = null,
    IReadOnlyList<ProtoTraceValue>? Values = null,
    ProtoTraceVisibility? Visibility = null,
    ProtoTraceRecord? Record = null);

/// <summary>Provides immutable snapshots of the automatic ProtoTest execution trace.</summary>
public interface IProtoTraceSource
{
    ProtoTraceRun Snapshot();

    /// <summary>
    /// Finds the test writer whose trace context an application span belongs to, or <see langword="null"/>
    /// when the span cannot be linked to a test - for example when the application is remote or emits
    /// different trace context. Application telemetry callbacks run outside the test's flow, so they
    /// cannot rely on the ambient test context; the W3C trace id is what links them back.
    /// </summary>
    IProtoTraceWriter? FindWriter(ActivityTraceId traceId);
}

/// <summary>
/// Records automatic execution operations and events. This API primarily exists for ProtoTest integrations.
/// </summary>
public interface IProtoTraceWriter
{
    ProtoTraceOperation StartOperation(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null,
        string? entityKind = null,
        string? entityId = null);

    /// <summary>
    /// Records an event. Identical error-free events written under the same parent collapse into one
    /// entry with a <see cref="ProtoTraceEntry.Count"/> in the trace, so a fact repeated on every call
    /// costs one row with a number instead of one row per call. Failures always stay separate.
    /// </summary>
    void WriteEvent(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        ProtoTraceOutcome outcome = ProtoTraceOutcome.Unknown,
        IReadOnlyDictionary<string, string?>? attributes = null,
        Exception? exception = null,
        string? parentId = null,
        string? entityKind = null,
        string? entityId = null);

    /// <summary>
    /// Records the current state of an entity, merging the fields into what is already known. Each call
    /// also appends a version, so a context that was set and later replaced keeps its history. State is
    /// recorded once per entity, not per call: entries refer to the entity instead of repeating it.
    /// </summary>
    void SetEntityState(
        string kind,
        string id,
        string name,
        IReadOnlyDictionary<string, string?>? state = null,
        string? scope = null,
        string? change = null);

    /// <summary>
    /// Records a value version: a domain object was created, changed, read or deleted. Versions accumulate
    /// on the value, and the version carries the operation that produced it.
    /// </summary>
    void Value(
        string kind,
        string id,
        string name,
        string change,
        IReadOnlyDictionary<string, string?>? state = null,
        ProtoTraceValueSource source = ProtoTraceValueSource.TestSide,
        bool inferred = false,
        string? scope = null,
        string? operationId = null);

    /// <summary>Records a semantic fact the test emitted on purpose; it belongs to the record axis.</summary>
    void Observation(
        string targetName,
        string kind,
        string identifier,
        string? data = null,
        string? metadata = null);

    /// <summary>Records an attachment; the artifact file itself is captured with the test's artifacts.</summary>
    void Attachment(string name, string mediaType, string? description = null);

    /// <summary>Records evidence worth reporting that is deliberately not a failure.</summary>
    void Finding(
        string message,
        string status,
        string category,
        string? targetName = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, object>? metadata = null);

    /// <summary>
    /// Captures a span from a watched activity source into the trace. Its tags become attributes, its
    /// status becomes the outcome, and its timing is preserved. This is how an application's own
    /// instrumentation reaches the run without depending on ProtoTest. Returns the id of the recorded
    /// operation so callers can link state to it.
    /// </summary>
    string? CaptureActivity(System.Diagnostics.Activity activity);
}

/// <summary>The final result reported by a test-framework adapter.</summary>
public sealed record ProtoTestResult(
    ProtoTraceOutcome Outcome,
    Exception? Exception = null,
    ProtoTraceError? Error = null)
{
    public static ProtoTestResult Unknown { get; } = new(ProtoTraceOutcome.Unknown);
    public static ProtoTestResult Passed { get; } = new(ProtoTraceOutcome.Succeeded);
    public static ProtoTestResult Skipped { get; } = new(ProtoTraceOutcome.Skipped);
    public static ProtoTestResult Failed(Exception exception) => new(ProtoTraceOutcome.Failed, exception);
    public static ProtoTestResult Failed(ProtoTraceError error) => new(ProtoTraceOutcome.Failed, Error: error);
    public static ProtoTestResult Cancelled(Exception? exception = null) => new(ProtoTraceOutcome.Cancelled, exception);
}
