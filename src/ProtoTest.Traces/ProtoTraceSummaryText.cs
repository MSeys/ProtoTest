namespace ProtoTest.Traces;

using System.Globalization;

/// <summary>
/// Renders the compact failure digest of a trace: the run, the outcome counts and, for every test that
/// did not fully succeed, its error, the source location of the failure and the failing operation. It is
/// the payload a CI log or an agent wants; the viewer remains the place for the whole story.
/// </summary>
public static class ProtoTraceSummaryText
{
    /// <summary>Writes the summary to a writer.</summary>
    public static void Write(ProtoTraceArchive archive, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(writer);

        var range = archive.RunStartedAtUtc is { } started
            ? archive.RunCompletedAtUtc is { } completed
                ? $"{started.ToUniversalTime():u} – {completed.ToUniversalTime():u}"
                : $"{started.ToUniversalTime():u} – (open)"
            : "unknown";
        writer.WriteLine($"ProtoTest trace {archive.FormatVersion} · run {archive.RunId} · {range}");

        var counts = archive.Tests
            .GroupBy(test => test.Outcome, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Count()} {group.Key}");
        writer.WriteLine($"{archive.Tests.Count} tests · {string.Join(" · ", counts)}");

        var failures = archive.Tests.Where(test => !test.Succeeded).ToArray();
        if (failures.Length == 0)
        {
            writer.WriteLine("All green.");
            return;
        }

        foreach (var test in failures)
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
        }
    }

    private static string FormatDuration(double milliseconds)
        => milliseconds < 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{milliseconds:F0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000:F2} s");
}
