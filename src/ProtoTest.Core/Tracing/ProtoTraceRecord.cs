namespace ProtoTest.Core;

/// <summary>
/// Deliberate evidence: what the test observed, attached, found and proved. The record axis is separate
/// from the timeline and the world; items reference the operation (and often the value) they belong to, and
/// never duplicate what those axes already carry.
/// </summary>
public sealed record ProtoTraceRecord(
    IReadOnlyList<ProtoTraceObservationRecord>? Observations = null,
    IReadOnlyList<ProtoTraceAttachmentRecord>? Attachments = null,
    IReadOnlyList<ProtoTraceFindingRecord>? Findings = null);

/// <summary>A semantic fact the test emitted on purpose.</summary>
public sealed record ProtoTraceObservationRecord(
    string Id,
    string? OperationId,
    DateTimeOffset AtUtc,
    string TargetName,
    string Kind,
    string Identifier,
    string? Data = null,
    string? Metadata = null);

/// <summary>An artifact the test attached; the file itself lives in the bundle's artifacts.</summary>
public sealed record ProtoTraceAttachmentRecord(
    string Id,
    string? OperationId,
    DateTimeOffset AtUtc,
    string Name,
    string MediaType,
    string? Description = null,
    string? ArtifactId = null,
    string? ArchivePath = null,
    long? SizeBytes = null,
    string? Error = null);

/// <summary>Evidence worth reporting that is deliberately not a failure.</summary>
public sealed record ProtoTraceFindingRecord(
    string Id,
    string? OperationId,
    DateTimeOffset AtUtc,
    string Message,
    string Status,
    string Category,
    string? TargetName = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, object>? Metadata = null);
