namespace ProtoTest.Traces;

/// <summary>One item of a fields, checks or diff section, as the archive records it.</summary>
public sealed record ProtoTraceSectionItem(
    string Label,
    string? Value,
    string? Detail,
    string Tone);

/// <summary>
/// A labelled block inside an operation: facts, a payload, check results or a diff. The kind and tone
/// are lower-case tokens (<c>fields</c>, <c>code</c>, <c>checks</c>, <c>diff</c>; <c>neutral</c>,
/// <c>success</c>, <c>warning</c>, <c>error</c>), the same normalization the viewer applies.
/// </summary>
public sealed record ProtoTraceSection(
    string Label,
    string Kind,
    IReadOnlyList<ProtoTraceSectionItem> Items,
    string? Content,
    string? Language);
