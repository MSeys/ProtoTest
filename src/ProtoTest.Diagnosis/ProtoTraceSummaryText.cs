namespace ProtoTest.Diagnosis;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Renders the compact failure digest of one run's diagnosis: the run, the outcome counts and, for
/// every test that did not fully succeed, its error, the source location of the selected failure, the
/// cause the diagnosis found, and the run gates that failed. It is the payload a CI log or an agent
/// wants; the viewer remains the place for the whole story.
/// </summary>
public static class ProtoTraceSummaryText
{
    private const int MaxPrintedMismatches = 3;

    private const int MaxPrintedGateDetails = 3;

    /// <summary>Writes the summary of one diagnosis document to a writer.</summary>
    public static void Write(ProtoDiagnosisDocument document, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        var range = document.StartedAtUtc is { } started
            ? document.CompletedAtUtc is { } completed
                ? $"{started.ToUniversalTime():u} - {completed.ToUniversalTime():u}"
                : $"{started.ToUniversalTime():u} - (open)"
            : "unknown";
        writer.WriteLine($"ProtoTest trace {document.TraceFormatVersion} · run {document.RunId} · {range}");

        var counts = document.Outcomes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Value} {pair.Key}");
        writer.WriteLine($"{document.Outcomes.Sum(pair => pair.Value)} tests · {string.Join(" · ", counts)}");

        var failedGates = document.Gates.Where(gate => string.Equals(gate.Verdict, "failed", StringComparison.Ordinal)).ToArray();
        if (document.Failures.Count == 0 && failedGates.Length == 0)
        {
            writer.WriteLine("All green.");
            return;
        }

        foreach (var test in document.Failures)
        {
            writer.WriteLine();
            writer.WriteLine($"{test.Outcome.ToUpperInvariant()} {test.Name} ({FormatDuration(test.DurationMs)})");
            var failure = test.Failure;
            if (failure?.ErrorMessage is { Length: > 0 } message)
            {
                foreach (var line in message.Split('\n'))
                {
                    writer.WriteLine($"  {line.TrimEnd('\r')}");
                }
            }

            if (failure?.SourceFile is { } file)
            {
                var line = failure.SourceLine is { } number ? $":{number}" : string.Empty;
                var function = failure.SourceFunction is { } name ? $" ({name})" : string.Empty;
                writer.WriteLine($"  at {file}{line}{function}");
            }

            if (failure is not null)
            {
                writer.WriteLine($"  {failure.Kind} {failure.Name} · {failure.Status}");
            }

            if (test.Rule is { } rule)
            {
                writer.WriteLine($"  cause: {Cause(rule, test)}");
            }
            else if (!string.Equals(test.Outcome, "skipped", StringComparison.Ordinal) && test.UnexplainedReason is { Length: > 0 } reason)
            {
                writer.WriteLine($"  unexplained: {reason}");
            }

            foreach (var finding in test.Findings)
            {
                writer.WriteLine($"  finding: {finding.Message} ({finding.Status} {finding.Category})");
            }

            foreach (var mismatch in test.Mismatches.Take(MaxPrintedMismatches))
            {
                writer.WriteLine($"  mismatch: {mismatch.Path}: expected {Value(mismatch.Expected)}, actual {Value(mismatch.Actual)}");
            }

            if (test.MismatchesTruncated)
            {
                writer.WriteLine($"  mismatch: ... more recorded, capped at {ProtoDiagnosis.MaxMismatches}");
            }
        }

        foreach (var gate in failedGates)
        {
            writer.WriteLine();
            writer.WriteLine($"RUN GATE FAILED {gate.Name}: {gate.Message}");
            foreach (var detail in gate.Details.Take(MaxPrintedGateDetails))
            {
                writer.WriteLine($"  {detail}");
            }
        }
    }

    private static string Cause(ProtoDiagnosisRule rule, ProtoDiagnosedTest test)
        => rule switch
        {
            ProtoDiagnosisRule.Assertion =>
                $"assertion ({test.Mismatches.Count} mismatch{(test.Mismatches.Count == 1 ? string.Empty : "es")})",
            ProtoDiagnosisRule.OperationError => "operation error",
            ProtoDiagnosisRule.RunnerFailure => "runner-reported failure",
            ProtoDiagnosisRule.Finding => "finding",
            _ => rule.ToString()
        };

    private static string Value(JsonElement? value)
        => value is null
            ? "null"
            : value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString() ?? "null"
                : value.Value.GetRawText();

    private static string FormatDuration(double milliseconds)
        => milliseconds < 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{milliseconds:F0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000:F2} s");
}
