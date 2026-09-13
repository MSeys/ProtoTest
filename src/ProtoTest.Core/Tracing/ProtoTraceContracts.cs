namespace ProtoTest.Core;

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
    ProtoTraceError? Error = null);

public sealed record ProtoTraceArtifact(
    string Id,
    string Name,
    string MediaType,
    string? Description,
    string ArchivePath,
    string? Error = null);

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
    IReadOnlyList<ProtoTraceArtifact> Artifacts);

public sealed record ProtoTraceRun(
    string FormatVersion,
    string RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<ProtoTestTrace> Tests,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<ProtoTraceArtifact>? Artifacts = null);

/// <summary>Provides immutable snapshots of the automatic ProtoTest execution trace.</summary>
public interface IProtoTraceSource
{
    ProtoTraceRun Snapshot();
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
        string? parentId = null);

    void WriteEvent(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        ProtoTraceOutcome outcome = ProtoTraceOutcome.Unknown,
        IReadOnlyDictionary<string, string?>? attributes = null,
        Exception? exception = null,
        string? parentId = null);
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
