namespace ProtoTest.Verification;

/// <summary>The finding classes a verification verdict carries. A class is an open string, so a later
/// comparison can add one without breaking a consumer that switches on the known four.</summary>
public static class ProtoVerificationFindingClasses
{
    /// <summary>A unit the baseline covered and the candidate reports uncovered.</summary>
    public const string Regressed = "regressed";

    /// <summary>A unit the candidate reports uncovered that the baseline did not have.</summary>
    public const string AddedUncovered = "added-uncovered";

    /// <summary>The recorded specification identity does not match the bytes a candidate carries, or the
    /// specification changed between the two runs.</summary>
    public const string StaleSpec = "stale-spec";

    /// <summary>A run gate that failed in the candidate run.</summary>
    public const string GateFailed = "gate-failed";
}

/// <summary>The severities a finding can carry. Only <see cref="Fail"/> fails the verdict.</summary>
public static class ProtoVerificationSeverities
{
    public const string Fail = "fail";
    public const string Warn = "warn";
    public const string Info = "info";
}

/// <summary>One deterministic verdict line over cross-run evidence.</summary>
/// <param name="Class">One of <see cref="ProtoVerificationFindingClasses"/>.</param>
/// <param name="Severity">One of <see cref="ProtoVerificationSeverities"/>.</param>
/// <param name="TargetName">The target the finding is about.</param>
/// <param name="Category">The category when the finding is about a coverage unit or a gate.</param>
/// <param name="Identifier">The unit or gate identifier when the finding has one.</param>
/// <param name="Message">A human-readable line naming the values.</param>
/// <param name="BaselineValue">The baseline's value, when the finding compares two.</param>
/// <param name="CurrentValue">The candidate's value, when the finding compares two.</param>
/// <param name="Evidence">A pointer to where the fact lives, naming the report or run.</param>
public sealed record ProtoVerificationFinding(
    string Class,
    string Severity,
    string TargetName,
    string? Category,
    string? Identifier,
    string Message,
    string? BaselineValue = null,
    string? CurrentValue = null,
    string? Evidence = null);
