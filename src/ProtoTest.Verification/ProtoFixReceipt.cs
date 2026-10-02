namespace ProtoTest.Verification;

using ProtoTest.Reporting;
using ProtoTest.Traces;

/// <summary>Why a fix is not proven. Each reason is one condition the recorded runs did not meet.</summary>
public static class ProtoFixReasons
{
    /// <summary>The baseline run did not record the test.</summary>
    public const string NotInBaseline = "not-in-baseline";

    /// <summary>The baseline run recorded the test as succeeded, so there was nothing to fix.</summary>
    public const string NotFailingInBaseline = "not-failing-in-baseline";

    /// <summary>A current run did not record the test.</summary>
    public const string NotInCurrent = "not-in-current";

    /// <summary>A current run recorded the test as not succeeded.</summary>
    public const string StillFailing = "still-failing";
}

/// <summary>One condition a proof did not meet: the reason code and a line that names the values.</summary>
public sealed record ProtoFixReason(string Code, string Message);

/// <summary>The proof for one test: proven, or the reasons it is not, and what changed in the test.</summary>
public sealed record ProtoFixProof(
    string Name,
    bool Proven,
    IReadOnlyList<ProtoFixReason> Reasons,
    ProtoOperationDivergence? Divergence);

/// <summary>
/// The receipt for a fix: the tests it claims, each proven or not, the tests that passed in the baseline
/// and fail now, and the report verdict when both runs embedded a report. The fix is proven only when
/// every claimed test is, nothing broke, and the report verdict has no fail finding.
/// </summary>
public sealed record ProtoFixReceipt(
    string BaselineRunId,
    IReadOnlyList<string> CurrentRunIds,
    IReadOnlyList<ProtoFixProof> Tests,
    IReadOnlyList<string> BrokenTests,
    ProtoVerificationVerdict? ReportVerdict,
    string? ReportNote)
{
    /// <summary>True when the receipt proves the fix.</summary>
    public bool Proven => Tests.Count > 0
        && Tests.All(test => test.Proven)
        && BrokenTests.Count == 0
        && ReportVerdict is not { Failed: true };
}

/// <summary>Builds a fix receipt from a baseline run and one or more current runs of the same suite.</summary>
internal static class ProtoFixProver
{
    public static ProtoFixReceipt Prove(
        ProtoTraceArchive baseline,
        IReadOnlyList<ProtoTraceArchive> currentRuns,
        IReadOnlyCollection<string>? testNames)
    {
        var comparisons = currentRuns.Select(current => ProtoTraceComparer.Compare(baseline, current)).ToArray();
        var claimed = testNames is { Count: > 0 }
            ? testNames.Distinct(StringComparer.Ordinal).ToArray()
            : [.. baseline.Tests.Where(ProtoTraceComparer.IsFailing).Select(test => test.Name).Distinct(StringComparer.Ordinal)];

        var proofs = claimed.Select(name => ProveTest(name, baseline, currentRuns, comparisons[0])).ToList();
        var broken = comparisons
            .SelectMany(comparison => comparison.Tests)
            .Where(test => test.Change == ProtoTestChanges.Broken)
            .Select(test => test.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var (verdict, note) = VerifyReports(baseline, currentRuns[^1]);
        return new ProtoFixReceipt(
            baseline.RunId,
            [.. currentRuns.Select(run => run.RunId)],
            proofs,
            broken,
            verdict,
            note);
    }

    private static ProtoFixProof ProveTest(
        string name,
        ProtoTraceArchive baseline,
        IReadOnlyList<ProtoTraceArchive> currentRuns,
        ProtoTraceComparison firstComparison)
    {
        var reasons = new List<ProtoFixReason>();
        var before = baseline.Tests.Where(test => test.Name == name).ToArray();
        if (before.Length == 0)
        {
            reasons.Add(new(ProtoFixReasons.NotInBaseline, $"Baseline run {baseline.RunId} did not record '{name}'."));
        }
        else if (!before.Any(ProtoTraceComparer.IsFailing))
        {
            reasons.Add(new(
                ProtoFixReasons.NotFailingInBaseline,
                $"'{name}' did not fail in baseline run {baseline.RunId} ({before[0].Outcome}), so the baseline does not show the failure the fix claims."));
        }

        foreach (var current in currentRuns)
        {
            var after = current.Tests.Where(test => test.Name == name).ToArray();
            if (after.Length == 0)
            {
                reasons.Add(new(ProtoFixReasons.NotInCurrent, $"Run {current.RunId} did not record '{name}'."));
            }
            else if (after.FirstOrDefault(test => !test.Succeeded) is { } failing)
            {
                reasons.Add(new(ProtoFixReasons.StillFailing, $"'{name}' was {failing.Outcome} in run {current.RunId}."));
            }
        }

        var divergence = firstComparison.Tests.FirstOrDefault(test => test.Name == name)?.Divergence;
        return new ProtoFixProof(name, reasons.Count == 0, reasons, divergence);
    }

    private static (ProtoVerificationVerdict? Verdict, string? Note) VerifyReports(ProtoTraceArchive baseline, ProtoTraceArchive current)
    {
        var baselineRun = ProtoVerificationRun.FromArchive(baseline);
        var currentRun = ProtoVerificationRun.FromArchive(current);
        if (baselineRun is null || currentRun is null)
        {
            var missing = baselineRun is null ? $"baseline run {baseline.RunId}" : $"run {current.RunId}";
            return (null, $"No JSON report is embedded in {missing}, so coverage and run gates were not compared.");
        }

        return (ProtoVerification.Verify(baselineRun, currentRun), null);
    }
}
