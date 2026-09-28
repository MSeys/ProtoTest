namespace ProtoTest.Verification;

using System.Globalization;

/// <summary>
/// Renders one verdict as the text a CI log reads: the verdict line, every finding, the per target and
/// category coverage deltas and the specification identity checks. Deterministic, and the failing
/// findings are the ones the CLI annotates.
/// </summary>
public static class ProtoVerificationText
{
    /// <summary>Writes the verdict to a writer.</summary>
    public static void Write(ProtoVerificationVerdict verdict, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(writer);

        var failing = verdict.Findings.Count(finding => finding.Severity == ProtoVerificationSeverities.Fail);
        var warnings = verdict.Findings.Count(finding => finding.Severity == ProtoVerificationSeverities.Warn);
        var infos = verdict.Findings.Count(finding => finding.Severity == ProtoVerificationSeverities.Info);
        writer.WriteLine(
            $"ProtoTest verification {(verdict.Failed ? "failed" : "passed")}: {failing} failing, {warnings} warning(s), {infos} info");

        foreach (var finding in verdict.Findings)
        {
            writer.WriteLine($"  {finding.Severity} {finding.Class}: {finding.Message}");
        }

        if (verdict.CoverageDeltas.Count > 0)
        {
            writer.WriteLine("coverage deltas:");
            foreach (var delta in verdict.CoverageDeltas)
            {
                writer.WriteLine(
                    $"  {delta.TargetName} · {delta.Category}: "
                    + $"{delta.Baseline.Covered}/{delta.Baseline.Total} covered -> {delta.Current.Covered}/{delta.Current.Total} covered "
                    + $"({delta.PercentageDelta.ToString("0.##", CultureInfo.InvariantCulture)} points, "
                    + $"{delta.Regressed} regressed, {delta.AddedUncovered} added uncovered)");
            }
        }

        if (verdict.SpecChecks.Count > 0)
        {
            writer.WriteLine("spec checks:");
            foreach (var check in verdict.SpecChecks)
            {
                writer.WriteLine($"  {check.TargetName} · {check.Category}: {check.Status}");
            }
        }
    }
}
