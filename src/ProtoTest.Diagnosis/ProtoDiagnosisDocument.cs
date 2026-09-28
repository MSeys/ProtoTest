namespace ProtoTest.Diagnosis;

using ProtoTest.Traces;

/// <summary>The rule that explains a non-succeeded test, in the order the diagnosis applies them.</summary>
public enum ProtoDiagnosisRule
{
    /// <summary>A failed assertion operation with recorded checks or mismatches.</summary>
    Assertion,

    /// <summary>A failed operation with an error type and message: a client, lifecycle or protocol call.</summary>
    OperationError,

    /// <summary>A runner-reported failure without a recorded operation error.</summary>
    RunnerFailure,

    /// <summary>A finding explains a test whose runner outcome stayed green.</summary>
    Finding
}

/// <summary>
/// The selected failure operation with the fields an agent reads: identity, phase, status, error,
/// source location, the subject and (in the context package) the recorded attributes.
/// </summary>
public sealed record ProtoDiagnosisOperation(
    string SpanId,
    string? ParentSpanId,
    string Kind,
    string Name,
    string Source,
    string Phase,
    string Status,
    string? ErrorType,
    string? ErrorMessage,
    string? SourceFile,
    int? SourceLine,
    string? SourceFunction,
    string? EntityKind,
    string? EntityId,
    string? Subject,
    IReadOnlyDictionary<string, string?>? Attributes,
    bool AttributesTruncated);

/// <summary>One evidence artifact of a test, with where the attachment recorded it and (on request) a content preview.</summary>
public sealed record ProtoDiagnosisArtifact(
    string Id,
    string Name,
    string MediaType,
    string? Description,
    string ArchivePath,
    long? SizeBytes,
    string? Error,
    string? OperationId,
    string? Content,
    bool ContentTruncated,
    string? ContentNote);

/// <summary>One finding the run or a test reported.</summary>
public sealed record ProtoDiagnosisFinding(
    string Message,
    string Status,
    string Category,
    string? TargetName,
    IReadOnlyList<string> Tags,
    string? TestId,
    string? TestName);

/// <summary>One run gate verdict, read from the report item or the run's own gate event.</summary>
public sealed record ProtoDiagnosisGate(
    string Name,
    string Verdict,
    string? Message,
    IReadOnlyList<string> Details);

/// <summary>The coverage totals the run's embedded report published.</summary>
public sealed record ProtoDiagnosisCoverage(
    int Total,
    int Covered,
    int Uncovered,
    double Percentage,
    string ReportArtifact,
    string ArchivePath,
    long? SizeBytes);

/// <summary>One non-succeeded test with the selected failure and its explanation.</summary>
public sealed record ProtoDiagnosedTest(
    string TestId,
    string Name,
    string? ClassName,
    string MethodName,
    string Outcome,
    double DurationMs,
    ProtoDiagnosisOperation? Failure,
    ProtoDiagnosisRule? Rule,
    string? UnexplainedReason,
    IReadOnlyList<ProtoTraceMismatch> Mismatches,
    bool MismatchesTruncated,
    IReadOnlyList<ProtoDiagnosisFinding> Findings,
    bool FindingsTruncated,
    IReadOnlyList<ProtoDiagnosisArtifact> Artifacts,
    bool ArtifactsTruncated);

/// <summary>
/// The diagnosis of one run: the digest the CLI, the MCP tools and the feedback channels consume.
/// Identity and outcome counts, the non-succeeded tests with their failure and explanation, the run
/// gates and findings, and the coverage the report published (or the reason there is none).
/// </summary>
public sealed record ProtoDiagnosisDocument(
    string DigestVersion,
    string TraceFormatVersion,
    string RunId,
    string? TraceFile,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyDictionary<string, string?> Environment,
    IReadOnlyDictionary<string, int> Outcomes,
    IReadOnlyList<ProtoDiagnosedTest> Failures,
    IReadOnlyList<ProtoDiagnosisGate> Gates,
    bool GatesTruncated,
    IReadOnlyList<ProtoDiagnosisFinding> Findings,
    bool FindingsTruncated,
    ProtoDiagnosisCoverage? Coverage,
    string? CoverageAbsentReason)
{
    /// <summary>The digest schema version; additive within 1.x.</summary>
    public static string CurrentDigestVersion => "1";
}
