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
        => Body(digest, new ProtoFeedbackTarget { TraceLink = traceLink });

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
        => Body(
            digest,
            new ProtoFeedbackTarget
            {
                TraceLink = traceLink,
                Comparison = comparison,
                Coverage = coverage,
                CoverageSuggestions = suggestions ?? []
            });

    /// <summary>
    /// Renders the comment body the post sends for the target: the summary card when the target names one
    /// and a comparison exists, the headline, what failed, the coverage that moved, and the per-test
    /// details folded away. Source locations link under the target's source address.
    /// </summary>
    public static string Body(ProtoDiagnosisDocument digest, ProtoFeedbackTarget target)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(target);
        return FeedbackCommentMarkdown.Render(digest, target);
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

        if (target.FromFork)
        {
            return Skipped("The pull request comes from a fork, whose workflow token cannot comment. The annotations and the job summary carry the evidence.");
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

            var body = Body(digest, target);
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
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? $"GitHub answered {Status(response)}. Give the workflow 'pull-requests: write' (or 'issues: write')."
                        : $"GitHub answered {Status(response)}.");
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

    private static bool HasChanges(ProtoTraceComparison? comparison)
        => comparison is not null && comparison.Tests.Any(test => test.Change is ProtoTestChanges.Broken or ProtoTestChanges.Fixed);

    private static ProtoFeedbackChannelResult Skipped(string reason)
        => new(ProtoFeedbackChannels.GithubPrComment, ProtoFeedbackStatuses.Skipped, reason);

    private static string Status(HttpResponseMessage response)
        => string.IsNullOrEmpty(response.ReasonPhrase)
            ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
            : $"{(int)response.StatusCode} {response.ReasonPhrase}";
}
