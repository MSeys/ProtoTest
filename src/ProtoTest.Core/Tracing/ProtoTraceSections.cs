namespace ProtoTest.Core;

/// <summary>
/// The closed vocabulary of operation sections. A viewer renders these four kinds and nothing else, so
/// an integration describes what happened in data, never in markup or styling.
/// </summary>
public enum ProtoTraceSectionKind
{
    /// <summary>Labelled facts: status, url, counts, timing.</summary>
    Fields,

    /// <summary>A payload to read: a body, a query, a snippet.</summary>
    Code,

    /// <summary>What was checked and how it went.</summary>
    Checks,

    /// <summary>Expected against actual, per property.</summary>
    Diff
}

public enum ProtoTraceSectionTone
{
    Neutral,
    Success,
    Warning,
    Error
}

/// <summary>
/// One item of a fields, checks or diff section. Fields use <see cref="Value"/>; checks use
/// <see cref="Value"/> and <see cref="Detail"/> with a <see cref="Tone"/>; diff uses
/// <see cref="Label"/> as the property path with expected in <see cref="Value"/> and actual in
/// <see cref="Detail"/>.
/// </summary>
public sealed record ProtoTraceSectionItem(
    string Label,
    string? Value = null,
    string? Detail = null,
    ProtoTraceSectionTone Tone = ProtoTraceSectionTone.Neutral);

/// <summary>
/// A labelled block inside an operation. Integrations attach sections where the data is known; the
/// viewer renders them under the operation, and the inspector reads the same sections deeper.
/// </summary>
public sealed record ProtoTraceSection(
    string Label,
    ProtoTraceSectionKind Kind,
    IReadOnlyList<ProtoTraceSectionItem>? Items = null,
    string? Content = null,
    string? Language = null);
