namespace ProtoTest.Verification;

using ProtoTest.Core;

/// <summary>The statuses a specification identity check can report.</summary>
public static class ProtoSpecCheckStatuses
{
    /// <summary>A candidate file was given and its content hash matches the recorded identity.</summary>
    public const string Verified = "verified";

    /// <summary>A candidate file was given and its content hash differs from the recorded identity.</summary>
    public const string Changed = "changed";

    /// <summary>The identity was recorded but not re-verified: no candidate file was given, or the
    /// source is remote and is deliberately not fetched.</summary>
    public const string Recorded = "recorded";

    /// <summary>The baseline recorded an identity for the target and the candidate report records none.</summary>
    public const string Missing = "missing";
}

/// <summary>One specification identity check for a target.</summary>
/// <param name="TargetName">The target the specification belongs to.</param>
/// <param name="Category">The collector category the identity item came from.</param>
/// <param name="Source">The recorded configured source; null when the candidate report records none.</param>
/// <param name="Hash">The recorded SHA-256 hash; null when the candidate report records none.</param>
/// <param name="BaselineHash">The baseline's recorded hash, when it recorded one.</param>
/// <param name="CandidateHash">The candidate file's hash, when one was given and readable.</param>
/// <param name="Status">One of <see cref="ProtoSpecCheckStatuses"/>.</param>
/// <param name="Message">A human-readable line naming the values.</param>
public sealed record ProtoSpecCheck(
    string TargetName,
    string Category,
    string? Source,
    string? Hash,
    string? BaselineHash,
    string? CandidateHash,
    string Status,
    string Message);

/// <summary>The candidate's coverage compared with the baseline, per target and category, using the
/// report's own coverage arithmetic.</summary>
/// <param name="TargetName">The target the row aggregates.</param>
/// <param name="Category">The category the row aggregates.</param>
/// <param name="Baseline">The baseline's covered and total units; 0 of 0 when it recorded none.</param>
/// <param name="Current">The candidate's covered and total units; 0 of 0 when it recorded none.</param>
/// <param name="Regressed">How many units this row covered in the baseline and not in the candidate.</param>
/// <param name="AddedUncovered">How many uncovered units this row did not have in the baseline.</param>
public sealed record ProtoVerificationCoverageDelta(
    string TargetName,
    string Category,
    ProtoCoverageSummary Baseline,
    ProtoCoverageSummary Current,
    int Regressed,
    int AddedUncovered)
{
    /// <summary>The percentage-point change, rounded as the report prints a percentage.</summary>
    public double PercentageDelta => Math.Round(Current.Percentage - Baseline.Percentage, 2);
}

/// <summary>The verification verdict: the findings that decide the gate, the per target and category
/// coverage deltas and the specification identity checks.</summary>
public sealed record ProtoVerificationVerdict(
    IReadOnlyList<ProtoVerificationFinding> Findings,
    IReadOnlyList<ProtoVerificationCoverageDelta> CoverageDeltas,
    IReadOnlyList<ProtoSpecCheck> SpecChecks)
{
    /// <summary>
    /// True when any finding carries <see cref="ProtoVerificationSeverities.Fail"/>. This is the gate:
    /// a CLI exits 1 and an action annotates the failing findings.
    /// </summary>
    public bool Failed => Findings.Any(finding => finding.Severity == ProtoVerificationSeverities.Fail);
}
