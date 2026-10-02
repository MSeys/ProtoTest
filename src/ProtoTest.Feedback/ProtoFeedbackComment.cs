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
    public static string Markdown(ProtoDiagnosisDocument digest, string? traceLink, ProtoTraceComparison? comparison)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var writer = new StringWriter();
        writer.WriteLine($"## ProtoTest run `{digest.RunId}`");
        writer.WriteLine();
        writer.WriteLine($"**{CountsLine(digest)}**");
        WriteComparison(writer, comparison);

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

        if (digest.Coverage is { } coverage)
        {
            writer.WriteLine();
            writer.WriteLine(
                $"Coverage: {coverage.Covered}/{coverage.Total} ({coverage.Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%)");
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

    /// <summary>
    /// Posts the comment for the digest. A missing token, repository, pull request number or failure
    /// skips with the reason; a reached API that refuses is a failed channel.
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

        if (!HasReport(digest) && !HasChanges(target.Comparison))
        {
            return Skipped("The run has no failures to report and changed no test's outcome.");
        }

        var api = target.ApiUrl ?? new Uri("https://api.github.com");
        var url = new Uri($"{api.AbsoluteUri.TrimEnd('/')}/repos/{target.Repository}/issues/{number}/comments");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { body = Markdown(digest, target.TraceLink, target.Comparison) }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("ProtoTest.Feedback");

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new ProtoFeedbackChannelResult(
                    ProtoFeedbackChannels.GithubPrComment, ProtoFeedbackStatuses.Posted, null)
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
