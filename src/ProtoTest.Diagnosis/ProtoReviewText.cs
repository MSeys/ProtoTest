namespace ProtoTest.Diagnosis;

using System.Globalization;

/// <summary>
/// Renders an evidence review as the text a CI log or an agent reads: the counts line, then each test
/// with findings, its findings and their next steps. Clean tests are counted, not listed.
/// </summary>
public static class ProtoReviewText
{
    /// <summary>Writes the review to a writer.</summary>
    public static void Write(ProtoRunReview review, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(writer);

        var clean = review.Tests.Count(test => test.Clean);
        var rules = review.Counts.Count == 0
            ? "no findings"
            : string.Join(" · ", review.Counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Value} {pair.Key}"));
        writer.WriteLine($"ProtoTest review: run {review.RunId}");
        writer.WriteLine($"{review.Tests.Count.ToString(CultureInfo.InvariantCulture)} tests · {clean.ToString(CultureInfo.InvariantCulture)} clean · {rules}");

        foreach (var test in review.Tests.Where(test => !test.Clean))
        {
            writer.WriteLine();
            writer.WriteLine($"{test.Name} ({test.Outcome}, {test.Checks.ToString(CultureInfo.InvariantCulture)} checks, {test.Calls.ToString(CultureInfo.InvariantCulture)} calls)");
            foreach (var finding in test.Findings)
            {
                writer.WriteLine($"  {finding.Rule}: {finding.Message}");
                if (finding.SourceFile is { Length: > 0 } file)
                {
                    writer.WriteLine($"    at {file}{(finding.SourceLine is { } line ? $":{line.ToString(CultureInfo.InvariantCulture)}" : string.Empty)}");
                }

                writer.WriteLine($"    next: {finding.Next}");
            }
        }
    }
}
