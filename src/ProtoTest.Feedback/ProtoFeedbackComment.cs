namespace ProtoTest.Feedback;

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProtoTest.Diagnosis;
using ProtoTest.Verification;

/// <summary>
/// The github-pr-comment channel: the Markdown rendering of the digest plus the composed trace link,
/// posted to the pull request the event payload named. The body is a pure rendering; the post itself
/// is a REST call to the issues comments API.
/// </summary>
public static class ProtoFeedbackComment
{
    /// <summary>Renders the comment body. One digest, so the comment cannot disagree with the CLI.</summary>
    public static string Markdown(ProtoDiagnosisDocument digest, string? traceLink = null)
        => Markdown(digest, traceLink, comparison: null);

    /// <summary>
    /// Renders the comment body with the comparison against the base branch: the tests the change broke
    /// first, then the ones it fixed, each with the operation where it left the baseline.
    /// </summary>
    public static string Markdown(
        ProtoDiagnosisDocument digest,
        string? traceLink,
        ProtoTraceComparison? comparison,
        ProtoVerificationVerdict? coverage = null,
        IReadOnlyList<ProtoCoverageSuggestion>? suggestions = null)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var writer = new StringWriter();
        writer.WriteLine(Marker);
        writer.WriteLine($"## ProtoTest run `{digest.RunId}`");
        writer.WriteLine();
        writer.WriteLine($"**{CountsLine(digest)}**");
        WriteComparison(writer, comparison);
        WriteCoverage(writer, coverage, suggestions ?? []);

        foreach (var test in digest.Failures)
        {
            writer.WriteLine();
            writer.WriteLine($"- **{test.Outcome.ToUpperInvariant()} `{test.Name}`** ({Duration(test.DurationMs)})");
            var failure = test.Failure;
            if (failure is not null)
            {
                // The name is the kind for assertion operations; print the identity once when they match.
                var identity = string.Equals(failure.Kind, failure.Name, StringComparison.Ordinal)
                    ? $"`{failure.Kind}`"
                    : $"`{failure.Kind}` `{failure.Name}`";
                writer.WriteLine($"  - {identity} · {failure.Status}");
            }

            if (failure?.ErrorMessage is { Length: > 0 } message)
            {
                foreach (var line in message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    writer.WriteLine($"  - {line}");
                }
            }

            if (failure?.SourceFile is { Length: > 0 } file)
            {
                var line = failure.SourceLine is { } number and > 0 ? $":{number}" : string.Empty;
                writer.WriteLine($"  - at `{file}{line}`");
            }

            if (failure?.Subject is { Length: > 0 } subject)
            {
                writer.WriteLine($"  - subject `{subject}`");
            }

            foreach (var mismatch in test.Mismatches)
            {
                writer.WriteLine($"  - mismatch `{mismatch.Path}`: expected {Value(mismatch.Expected)}, actual {Value(mismatch.Actual)}");
            }

            if (test.MismatchesTruncated)
            {
                writer.WriteLine($"  - ... more recorded mismatches, capped at {ProtoDiagnosis.MaxMismatches}");
            }

            foreach (var finding in test.Findings)
            {
                writer.WriteLine($"  - finding: {finding.Message} ({finding.Status} {finding.Category})");
            }

            if (test.Rule is null
                && !string.Equals(test.Outcome, "skipped", StringComparison.Ordinal)
                && test.UnexplainedReason is { Length: > 0 } reason)
            {
                writer.WriteLine($"  - unexplained: {reason}");
            }
        }

        var failedGates = digest.Gates
            .Where(gate => string.Equals(gate.Verdict, "failed", StringComparison.Ordinal))
            .ToArray();
        if (failedGates.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("**Run gates**");
            foreach (var gate in failedGates)
            {
                writer.WriteLine(gate.Message is { Length: > 0 } message
                    ? $"- `{gate.Name}` failed: {message}"
                    : $"- `{gate.Name}` failed.");
            }
        }

        if (digest.Coverage is { } totals)
        {
            writer.WriteLine();
            writer.WriteLine(
                $"Coverage: {totals.Covered}/{totals.Total} ({totals.Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%)");
        }

        writer.WriteLine();
        if (traceLink is { Length: > 0 } link)
        {
            writer.WriteLine($"[Full trace]({link})");
        }
        else if (digest.TraceFile is { Length: > 0 } traceFile)
        {
            writer.WriteLine($"Trace: `{traceFile}`");
        }

        return writer.ToString();
    }

    /// <summary>The first line of every comment, so a later run finds the comment to update.</summary>
    public const string Marker = "<!-- prototest-evidence -->";

    /// <summary>
    /// Posts the comment for the digest, or updates the one an earlier run of the same pull request
    /// posted, so a pull request carries one ProtoTest comment. A run with nothing to say updates an
    /// existing comment to its green state and otherwise skips. A missing token, repository or pull
    /// request number skips with the reason; a reached API that refuses is a failed channel.
    /// </summary>
    public static async Task<ProtoFeedbackChannelResult> PostAsync(
        ProtoDiagnosisDocument digest,
        ProtoFeedbackTarget target,
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(target.Token))
        {
            return Skipped("No GitHub token: set GITHUB_TOKEN.");
        }

        if (string.IsNullOrWhiteSpace(target.Repository))
        {
            return Skipped("No GitHub repository: set GITHUB_REPOSITORY.");
        }

        if (target.PullRequestNumber is not { } number)
        {
            return Skipped("No pull request number: set GITHUB_EVENT_PATH to the event payload of a pull request run.");
        }

        var api = (target.ApiUrl ?? new Uri("https://api.github.com")).AbsoluteUri.TrimEnd('/');
        var root = $"{api}/repos/{target.Repository}";
        var hasNews = HasReport(digest) || HasChanges(target.Comparison) || HasCoverageNews(target.Coverage);

        try
        {
            var existing = await FindExistingAsync(client, target, $"{root}/issues/{number}/comments", cancellationToken).ConfigureAwait(false);
            if (!hasNews && existing is null)
            {
                return Skipped("The run has no failures to report, changed no test's outcome and left coverage as it was.");
            }

            var body = Markdown(digest, target.TraceLink, target.Comparison, target.Coverage, target.CoverageSuggestions);
            using var request = Request(
                existing is { } id ? HttpMethod.Patch : HttpMethod.Post,
                existing is { } commentId ? $"{root}/issues/comments/{commentId}" : $"{root}/issues/{number}/comments",
                target);
            request.Content = new StringContent(JsonSerializer.Serialize(new { body }), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new ProtoFeedbackChannelResult(
                    ProtoFeedbackChannels.GithubPrComment,
                    ProtoFeedbackStatuses.Posted,
                    existing is { } updated ? $"Updated comment {updated}." : null)
                : new ProtoFeedbackChannelResult(
                    ProtoFeedbackChannels.GithubPrComment,
                    ProtoFeedbackStatuses.Failed,
                    $"GitHub answered {Status(response)}.");
        }
        catch (HttpRequestException exception)
        {
            return new ProtoFeedbackChannelResult(
                ProtoFeedbackChannels.GithubPrComment,
                ProtoFeedbackStatuses.Failed,
                $"The comment post failed: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProtoFeedbackChannelResult(
                ProtoFeedbackChannels.GithubPrComment, ProtoFeedbackStatuses.Failed, "The comment post timed out.");
        }
    }

    /// <summary>True when the digest carries a test failure or a failed run gate.</summary>
    public static bool HasReport(ProtoDiagnosisDocument digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        return digest.Failures.Any(test => !string.Equals(test.Outcome, "skipped", StringComparison.Ordinal))
            || digest.Gates.Any(gate => string.Equals(gate.Verdict, "failed", StringComparison.Ordinal));
    }

    // The comment an earlier run posted carries the marker; a list GitHub cannot give is treated as none.
    private static async Task<long?> FindExistingAsync(
        HttpClient client,
        ProtoFeedbackTarget target,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"{url}?per_page=100", target);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var comment in document.RootElement.EnumerateArray())
            {
                if (comment.TryGetProperty("body", out var body)
                    && body.GetString()?.StartsWith(Marker, StringComparison.Ordinal) == true
                    && comment.TryGetProperty("id", out var id)
                    && id.TryGetInt64(out var value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, ProtoFeedbackTarget target)
    {
        var request = new HttpRequestMessage(method, new Uri(url));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("ProtoTest.Feedback");
        return request;
    }

    private static bool HasCoverageNews(ProtoVerificationVerdict? coverage)
        => coverage is not null && coverage.Findings.Any(finding =>
            finding.Class is ProtoVerificationFindingClasses.Regressed or ProtoVerificationFindingClasses.AddedUncovered);

    // What moved against the base branch: the target and category rows that changed, then the units the
    // change added without a test (with where to cover each) and the ones it stopped covering.
    private static void WriteCoverage(
        StringWriter writer,
        ProtoVerificationVerdict? coverage,
        IReadOnlyList<ProtoCoverageSuggestion> suggestions)
    {
        if (coverage is null)
        {
            return;
        }

        var moved = coverage.CoverageDeltas
            .Where(delta => delta.Regressed > 0 || delta.AddedUncovered > 0 || delta.PercentageDelta != 0)
            .ToArray();
        var added = coverage.Findings.Where(finding => finding.Class == ProtoVerificationFindingClasses.AddedUncovered).ToArray();
        var regressed = coverage.Findings.Where(finding => finding.Class == ProtoVerificationFindingClasses.Regressed).ToArray();
        writer.WriteLine();
        if (moved.Length == 0 && added.Length == 0 && regressed.Length == 0)
        {
            writer.WriteLine("Coverage against the base branch: unchanged.");
            return;
        }

        writer.WriteLine("**Coverage against the base branch**");
        foreach (var delta in moved.Take(MaxComparedTests))
        {
            var points = delta.PercentageDelta.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
            writer.WriteLine(
                $"- `{delta.TargetName}` · {delta.Category}: {delta.Baseline.Covered}/{delta.Baseline.Total} → {delta.Current.Covered}/{delta.Current.Total} ({points} points)");
        }

        foreach (var finding in added.Take(MaxComparedTests))
        {
            var suggestion = suggestions.FirstOrDefault(candidate =>
                string.Equals(candidate.Target, finding.TargetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Category, finding.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Identifier, finding.Identifier, StringComparison.Ordinal));
            var hint = suggestion is null ? string.Empty : $" {suggestion.Reason}";
            writer.WriteLine($"- new and uncovered: `{finding.Identifier}` ({finding.TargetName} · {finding.Category}).{hint}");
        }

        foreach (var finding in regressed.Take(MaxComparedTests))
        {
            writer.WriteLine($"- no longer covered: `{finding.Identifier}` ({finding.TargetName} · {finding.Category})");
        }

        var hidden = Math.Max(0, added.Length - MaxComparedTests) + Math.Max(0, regressed.Length - MaxComparedTests);
        if (hidden > 0)
        {
            writer.WriteLine($"- ... {hidden} more units; the job summary lists them all");
        }
    }

    private static bool HasChanges(ProtoTraceComparison? comparison)
        => comparison is not null && comparison.Tests.Any(test => test.Change is ProtoTestChanges.Broken or ProtoTestChanges.Fixed);

    private static void WriteComparison(StringWriter writer, ProtoTraceComparison? comparison)
    {
        if (comparison is null)
        {
            return;
        }

        var changed = comparison.Tests
            .Where(test => test.Change is ProtoTestChanges.Broken or ProtoTestChanges.Fixed)
            .ToArray();
        writer.WriteLine();
        if (changed.Length == 0)
        {
            writer.WriteLine($"Compared with the base branch (run `{comparison.BaselineRunId}`): no test changed outcome.");
            return;
        }

        writer.WriteLine($"**Compared with the base branch** (run `{comparison.BaselineRunId}`)");
        foreach (var test in changed.Take(MaxComparedTests))
        {
            var where = test.Divergence?.Current ?? test.Divergence?.Baseline;
            var at = where is null
                ? string.Empty
                : where.Subject is { Length: > 0 } subject
                    ? $" at `{where.Kind}` `{subject}`"
                    : $" at `{where.Kind}` `{where.Name}`";
            writer.WriteLine($"- {(test.Change == ProtoTestChanges.Broken ? "broke" : "fixed")} `{test.Name}`{at}");
        }

        if (changed.Length > MaxComparedTests)
        {
            writer.WriteLine($"- ... {changed.Length - MaxComparedTests} more");
        }
    }

    private const int MaxComparedTests = 20;

    private static ProtoFeedbackChannelResult Skipped(string reason)
        => new(ProtoFeedbackChannels.GithubPrComment, ProtoFeedbackStatuses.Skipped, reason);

    private static string Status(HttpResponseMessage response)
        => string.IsNullOrEmpty(response.ReasonPhrase)
            ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
            : $"{(int)response.StatusCode} {response.ReasonPhrase}";

    private static string CountsLine(ProtoDiagnosisDocument digest)
    {
        var total = digest.Outcomes.Sum(pair => pair.Value);
        var parts = new List<string> { $"{total} tests" };
        parts.AddRange(digest.Outcomes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Value} {pair.Key}"));
        return string.Join(" · ", parts);
    }

    private static string Duration(double milliseconds)
        => milliseconds < 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{milliseconds:F0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000:F2} s");

    private static string Value(JsonElement? value)
        => value is null
            ? "null"
            : value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString() ?? "null"
                : value.Value.GetRawText();
}
