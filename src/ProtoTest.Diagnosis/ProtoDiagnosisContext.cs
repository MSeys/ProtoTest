namespace ProtoTest.Diagnosis;

using ProtoTest.Traces;

/// <summary>One item of a section preview; a value or detail over the preview cap carries a truncation note.</summary>
public sealed record ProtoDiagnosisSectionItem(
    string Label,
    string? Value,
    string? Detail,
    string Tone);

/// <summary>One operation section with payload previews: fields, code, checks or diff.</summary>
public sealed record ProtoDiagnosisSection(
    string Label,
    string Kind,
    IReadOnlyList<ProtoDiagnosisSectionItem> Items,
    bool ItemsTruncated,
    string? Content,
    string? Language,
    bool ContentTruncated);

/// <summary>One numbered line of an embedded source snippet.</summary>
public sealed record ProtoDiagnosisSourceLine(int Number, string Text);

/// <summary>The embedded source window around the failing line.</summary>
public sealed record ProtoDiagnosisSource(
    string File,
    int Line,
    int StartLine,
    int EndLine,
    IReadOnlyList<ProtoDiagnosisSourceLine> Lines);

/// <summary>One state change the failing operation caused.</summary>
public sealed record ProtoDiagnosisStateChange(
    string Change,
    DateTimeOffset? AtUtc,
    string Source,
    bool Inferred,
    IReadOnlyDictionary<string, string?> State);

/// <summary>A tracked item the failing operation acted on or changed, with the changes it caused.</summary>
public sealed record ProtoDiagnosisStateItem(
    string Kind,
    string Id,
    string Name,
    IReadOnlyDictionary<string, string?> State,
    IReadOnlyList<ProtoDiagnosisStateChange> Changes,
    bool ChangesTruncated);

/// <summary>One coverage row of the report the operation's subject touched.</summary>
public sealed record ProtoDiagnosisCoverageRow(
    string Target,
    string Category,
    string Identifier,
    string Status,
    int Count,
    bool IsCovered,
    string? Message,
    string? DisplayName);

/// <summary>The report context of one test: coverage, gates and findings, or the reason there is no report.</summary>
public sealed record ProtoDiagnosisReportContext(
    ProtoDiagnosisCoverage? Coverage,
    IReadOnlyList<ProtoDiagnosisCoverageRow> CoverageRows,
    bool CoverageRowsTruncated,
    IReadOnlyList<ProtoDiagnosisFinding> Findings,
    bool FindingsTruncated,
    IReadOnlyList<ProtoDiagnosisGate> Gates,
    bool GatesTruncated,
    string? AbsentReason);

/// <summary>
/// What an agent receives for one test: the failure entry, the ancestor chain and the nearest call,
/// the operation's section previews, the embedded source snippet, its artifacts, the state it changed
/// and the report context. Every list is capped; a truncated list says so.
/// </summary>
public sealed record ProtoDiagnosisContext(
    string DigestVersion,
    string RunId,
    string? TraceFile,
    string TestId,
    string TestName,
    string? ClassName,
    string MethodName,
    string Outcome,
    ProtoDiagnosisRule? Rule,
    string? UnexplainedReason,
    ProtoDiagnosisOperation? Failure,
    IReadOnlyList<ProtoTraceMismatch> Mismatches,
    bool MismatchesTruncated,
    IReadOnlyList<ProtoDiagnosisFinding> Findings,
    bool FindingsTruncated,
    IReadOnlyList<ProtoDiagnosisOperation> Ancestors,
    bool AncestorsTruncated,
    ProtoDiagnosisOperation? Call,
    IReadOnlyList<ProtoDiagnosisSection> Sections,
    bool SectionsTruncated,
    ProtoDiagnosisSource? Source,
    string? SourceAbsentReason,
    IReadOnlyList<ProtoDiagnosisArtifact> Artifacts,
    bool ArtifactsTruncated,
    IReadOnlyList<ProtoDiagnosisStateItem> State,
    bool StateTruncated,
    string? StateAbsentReason,
    ProtoDiagnosisReportContext Report);
