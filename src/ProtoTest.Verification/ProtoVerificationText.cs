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

    /// <summary>
    /// Writes a run comparison: the counts line, then every test that did not stay the same with the
    /// operation where its two recordings part. Unchanged tests are counted, not listed.
    /// </summary>
    public static void Write(ProtoTraceComparison comparison, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(writer);

        string[] classes =
        [
            ProtoTestChanges.Broken, ProtoTestChanges.Fixed, ProtoTestChanges.StillFailing,
            ProtoTestChanges.New, ProtoTestChanges.Removed, ProtoTestChanges.Unchanged
        ];
        var counts = classes
            .Where(change => comparison.Counts.ContainsKey(change))
            .Select(change => $"{comparison.Counts[change]} {change}");
        writer.WriteLine($"ProtoTest comparison: run {comparison.BaselineRunId} -> run {comparison.CurrentRunId}");
        writer.WriteLine($"{comparison.Tests.Count} tests · {string.Join(" · ", counts)}");

        foreach (var test in comparison.Tests.Where(test => test.Change != ProtoTestChanges.Unchanged))
        {
            writer.WriteLine();
            writer.WriteLine($"{test.Change.ToUpperInvariant()} {test.Name} ({test.BaselineOutcome ?? "absent"} -> {test.CurrentOutcome ?? "absent"})");
            if (test.Divergence is not { } divergence)
            {
                if (test.Change == ProtoTestChanges.StillFailing)
                {
                    writer.WriteLine("  fails the same way: no operation changed status or error type");
                }

                continue;
            }

            writer.WriteLine($"  diverges at: {Operation(divergence.Current ?? divergence.Baseline!)} ({divergence.Reason})");
            if (divergence.Baseline is { } before)
            {
                writer.WriteLine($"    baseline: {Outcome(before)}");
            }

            if (divergence.Current is { } after)
            {
                writer.WriteLine($"    current:  {Outcome(after)}");
            }

            var located = divergence.Current ?? divergence.Baseline!;
            if (located.SourceFile is { Length: > 0 } file)
            {
                writer.WriteLine($"    at {file}{(located.SourceLine is { } line ? $":{line}" : string.Empty)}");
            }
        }
    }

    /// <summary>
    /// Writes a fix receipt: the verdict line, each claimed test with its reasons or where it changed,
    /// the tests that broke, and the report verdict's failing findings.
    /// </summary>
    public static void Write(ProtoFixReceipt receipt, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(writer);

        var runs = receipt.CurrentRunIds.Count == 1 ? "run" : "runs";
        writer.WriteLine(
            $"ProtoTest fix {(receipt.Proven ? "proven" : "not proven")}: baseline run {receipt.BaselineRunId} -> {runs} {string.Join(", ", receipt.CurrentRunIds)}");
        if (receipt.Tests.Count == 0)
        {
            writer.WriteLine("  no claimed test: the baseline recorded no failing test and none was named");
        }

        foreach (var test in receipt.Tests)
        {
            writer.WriteLine($"  {(test.Proven ? "PROVEN" : "NOT PROVEN")} {test.Name}");
            foreach (var reason in test.Reasons)
            {
                writer.WriteLine($"    {reason.Code}: {reason.Message}");
            }

            if (test.Divergence is { } divergence)
            {
                writer.WriteLine($"    changed at: {Operation(divergence.Current ?? divergence.Baseline!)} ({divergence.Reason})");
            }
        }

        foreach (var name in receipt.BrokenTests)
        {
            writer.WriteLine($"  BROKEN {name}: succeeded in the baseline and fails now");
        }

        if (receipt.ReportVerdict is { } verdict)
        {
            foreach (var finding in verdict.Findings.Where(finding => finding.Severity == ProtoVerificationSeverities.Fail))
            {
                writer.WriteLine($"  REPORT {finding.Class}: {finding.Message}");
            }
        }
        else if (receipt.ReportNote is { Length: > 0 } note)
        {
            writer.WriteLine($"  note: {note}");
        }
    }

    private static string Operation(ProtoComparedOperation operation)
        => operation.Subject is { Length: > 0 } subject
            ? $"{operation.Kind} {operation.Name} [{subject}]"
            : $"{operation.Kind} {operation.Name}";

    private static string Outcome(ProtoComparedOperation operation)
    {
        var error = operation.ErrorType ?? (operation.ErrorMessage is null ? null : "error");
        if (error is null)
        {
            return operation.Status;
        }

        var message = operation.ErrorMessage is { Length: > 0 } text ? $": {FirstLine(text)}" : string.Empty;
        return $"{operation.Status} · {error}{message}";
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].TrimEnd('\r');
        return line.Length <= 200 ? line : string.Concat(line.AsSpan(0, 200), "...");
    }
}
