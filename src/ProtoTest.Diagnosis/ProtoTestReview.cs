namespace ProtoTest.Diagnosis;

using System.Globalization;
using ProtoTest.Traces;

/// <summary>The rules an evidence review applies to one test. Each one is read from the recorded trace.</summary>
public static class ProtoReviewRules
{
    /// <summary>The test body recorded no check, so it passes as long as nothing throws.</summary>
    public const string NoCheck = "no-check";

    /// <summary>A protocol call whose result no check looked at before the next call.</summary>
    public const string UncheckedCall = "unchecked-call";

    /// <summary>Time in the test body with no recorded operation: a sleep or an untraced call.</summary>
    public const string UntracedGap = "untraced-gap";
}

/// <summary>
/// One thing an evidence review found in a test: the rule, what was recorded, the next step, and where
/// in the source it points when the trace captured a location.
/// </summary>
public sealed record ProtoReviewFinding(
    string Rule,
    string Message,
    string Next,
    string? Subject,
    string? SourceFile,
    int? SourceLine);

/// <summary>One test's review: its outcome, the checks it recorded, and the findings.</summary>
public sealed record ProtoTestReview(
    string TestId,
    string Name,
    string Outcome,
    int Checks,
    int Calls,
    IReadOnlyList<ProtoReviewFinding> Findings)
{
    /// <summary>True when the review found nothing: the test checks what it calls and records its time.</summary>
    public bool Clean => Findings.Count == 0;
}

/// <summary>The evidence review of a run: one entry per reviewed test, most findings first.</summary>
public sealed record ProtoRunReview(string RunId, IReadOnlyList<ProtoTestReview> Tests)
{
    /// <summary>How many findings each rule produced across the run.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; } = Tests
        .SelectMany(test => test.Findings)
        .GroupBy(finding => finding.Rule, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}

/// <summary>
/// Reviews what each test proves from what it recorded. It looks only at the test body (the execution
/// phase): a body with no check, a call no check looked at, and time with no recorded operation, the
/// same untraced gap the viewer draws. Every finding carries the next step.
/// </summary>
internal static class ProtoTestReviewer
{
    /// <summary>A gap shorter than this is timing noise, not a sleep or an untraced call.</summary>
    private static readonly TimeSpan MinimumGap = TimeSpan.FromMilliseconds(250);

    public static ProtoRunReview Review(ProtoTraceArchive archive, IReadOnlyCollection<string>? testNames)
    {
        var tests = archive.Tests.AsEnumerable();
        if (testNames is { Count: > 0 })
        {
            var wanted = new HashSet<string>(testNames, StringComparer.Ordinal);
            tests = tests.Where(test => wanted.Contains(test.Name) || wanted.Contains(test.TestId));
        }

        var reviews = tests
            .Select(ReviewTest)
            .OrderByDescending(review => review.Findings.Count)
            .ThenBy(review => review.Name, StringComparer.Ordinal)
            .ToList();
        return new ProtoRunReview(archive.RunId, reviews);
    }

    private static ProtoTestReview ReviewTest(ProtoTraceTest test)
    {
        var body = test.Operations
            .Where(operation => operation.Phase == "execution" && !operation.Kind.StartsWith("test.", StringComparison.Ordinal))
            .OrderBy(operation => operation.StartedAtUtc)
            .ToList();
        var checks = body.Where(IsCheck).ToList();
        var calls = body.Where(operation => ProtoTraceOperation.IsCall(operation.Kind) && !IsCheck(operation)).ToList();
        var findings = new List<ProtoReviewFinding>();

        if (checks.Count == 0 && body.Count > 0)
        {
            var first = calls.FirstOrDefault() ?? body[0];
            findings.Add(new ProtoReviewFinding(
                ProtoReviewRules.NoCheck,
                $"The test body recorded {Count(body.Count, "operation")} and no check, so it passes as long as nothing throws.",
                "Assert on what the test is about: Should.HaveStatus, Should.MatchShape or a message await, so a wrong answer fails the test.",
                first.Subject,
                first.SourceFile,
                first.SourceLine));
        }
        else
        {
            foreach (var call in calls.Where(call => !IsChecked(call, test, body, checks)))
            {
                findings.Add(new ProtoReviewFinding(
                    ProtoReviewRules.UncheckedCall,
                    $"'{call.Subject ?? call.Name}' was called and no check looked at its result before the next call.",
                    "Check what the call returned, or move it into setup if the test only needs its side effect.",
                    call.Subject,
                    call.SourceFile,
                    call.SourceLine));
            }
        }

        foreach (var (start, duration, before) in Gaps(test))
        {
            var at = (start - (test.Operations.FirstOrDefault(operation => operation.Kind == "test.execution")?.StartedAtUtc ?? start)).TotalMilliseconds;
            findings.Add(new ProtoReviewFinding(
                ProtoReviewRules.UntracedGap,
                $"{Milliseconds(duration.TotalMilliseconds)} of the test body recorded no operation, starting {Milliseconds(at)} in"
                    + (before is null ? ", until the body ended." : $", before '{before.Subject ?? before.Name}'."),
                "Replace a sleep with a wait that records what it waits for (ProtoPolling, a message await, the test clock), or call through a ProtoTest client so the call is traced.",
                before?.Subject,
                before?.SourceFile,
                before?.SourceLine));
        }

        return new ProtoTestReview(test.TestId, test.Name, test.Outcome, checks.Count, calls.Count, findings);
    }

    /// <summary>A check fails the test when the answer is wrong: an assertion, or an await for a message.</summary>
    private static bool IsCheck(ProtoTraceOperation operation)
        => operation.Kind.StartsWith("assert.", StringComparison.Ordinal)
            || operation.Kind.EndsWith(".await", StringComparison.Ordinal)
            || operation.Kind.EndsWith(".receive", StringComparison.Ordinal);

    // A call is checked by an assertion inside it, or by one that starts after it and before the next call.
    private static bool IsChecked(
        ProtoTraceOperation call,
        ProtoTraceTest test,
        IReadOnlyList<ProtoTraceOperation> body,
        IReadOnlyList<ProtoTraceOperation> checks)
    {
        if (checks.Any(check => test.Ancestors(check).Any(ancestor => ReferenceEquals(ancestor, call))))
        {
            return true;
        }

        var next = body.FirstOrDefault(operation => operation.StartedAtUtc > call.StartedAtUtc
            && ProtoTraceOperation.IsCall(operation.Kind)
            && !IsCheck(operation)
            && !test.Ancestors(operation).Any(ancestor => ReferenceEquals(ancestor, call)));
        return checks.Any(check => check.StartedAtUtc >= call.StartedAtUtc
            && (next is null || check.StartedAtUtc < next.StartedAtUtc));
    }

    // The viewer's rule: a gap inside a phase span longer than min(250 ms, max(20 ms, 15% of the phase)).
    // A review reports only body gaps of at least 250 ms, so short scheduling noise never becomes a finding.
    private static IEnumerable<(DateTimeOffset Start, TimeSpan Duration, ProtoTraceOperation? Before)> Gaps(ProtoTraceTest test)
    {
        var lifecycle = test.Operations.FirstOrDefault(operation => operation.Kind == "test.execution");
        if (lifecycle?.DurationMs is not { } lifecycleMs)
        {
            yield break;
        }

        var threshold = TimeSpan.FromMilliseconds(Math.Max(MinimumGap.TotalMilliseconds, Math.Min(250, Math.Max(20, lifecycleMs * 0.15))));
        var end = lifecycle.StartedAtUtc.AddMilliseconds(lifecycleMs);
        var children = test.Operations
            .Where(operation => operation.ParentSpanId == lifecycle.SpanId)
            .OrderBy(operation => operation.StartedAtUtc)
            .ToList();
        var cursor = lifecycle.StartedAtUtc;
        foreach (var child in children)
        {
            if (child.StartedAtUtc - cursor >= threshold)
            {
                yield return (cursor, child.StartedAtUtc - cursor, child);
            }

            var childEnd = child.StartedAtUtc.AddMilliseconds(child.DurationMs ?? 0);
            if (childEnd > cursor)
            {
                cursor = childEnd;
            }
        }

        if (end - cursor >= threshold)
        {
            yield return (cursor, end - cursor, null);
        }
    }

    private static string Count(int count, string noun)
        => $"{count.ToString(CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";

    private static string Milliseconds(double value)
        => value >= 1000
            ? $"{(value / 1000).ToString("0.0", CultureInfo.InvariantCulture)} s"
            : $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)} ms";
}
