namespace ProtoTest.Traces;

/// <summary>Something that happened at a moment inside an operation: a server started, a subscription got a message.</summary>
public sealed record ProtoTraceMoment(
    string Name,
    DateTimeOffset AtUtc,
    string Kind,
    string Source,
    string Outcome,
    string? ErrorType,
    string? ErrorMessage,
    string? EntityKind,
    string? EntityId,
    IReadOnlyDictionary<string, string?> Attributes,
    IReadOnlyList<ProtoTraceSection> Sections);

/// <summary>
/// An observation, attachment or finding the run recorded, on an operation when the trace names one and
/// on the test or run otherwise. <see cref="Record"/> is <c>observation</c>, <c>attachment</c> or
/// <c>finding</c>; the fields a record kind does not carry stay null.
/// </summary>
public sealed record ProtoTraceRecordEvent(
    string Record,
    string Name,
    DateTimeOffset AtUtc,
    string? Kind,
    string? Identifier,
    string? Data,
    IReadOnlyDictionary<string, string?> Metadata,
    string? ArtifactId,
    string? Status,
    string? Category,
    string? TargetName,
    IReadOnlyList<string> Tags);
