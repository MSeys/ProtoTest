namespace ProtoTest.Feedback;

using ProtoTest.Diagnosis;
using ProtoTest.Traces;
using ProtoTest.Verification;

/// <summary>The channel names a feedback post reports its outcome under.</summary>
public static class ProtoFeedbackChannels
{
    /// <summary>The workflow command lines for the digest's failing tests and failed run gates.</summary>
    public const string GithubAnnotations = "github-annotations";

    /// <summary>The pull request comment carrying the digest and the composed trace link.</summary>
    public const string GithubPrComment = "github-pr-comment";

    /// <summary>The digest JSON posted to the configured address.</summary>
    public const string Webhook = "webhook";
}

/// <summary>The outcome one channel reports.</summary>
public static class ProtoFeedbackStatuses
{
    /// <summary>The channel delivered its payload.</summary>
    public const string Posted = "posted";

    /// <summary>The channel had no target or nothing to report; the reason names which.</summary>
    public const string Skipped = "skipped";

    /// <summary>The channel reached its target and the target refused or did not answer.</summary>
    public const string Failed = "failed";
}

/// <summary>One channel's outcome: what it did and, when it did not post, why.</summary>
public sealed record ProtoFeedbackChannelResult(string Channel, string Status, string? Reason);

/// <summary>
/// The result of one feedback post. Every channel runs; a channel that cannot post skips with its
/// reason, and a channel that reached its target and failed is named here.
/// </summary>
public sealed record ProtoFeedbackReport(IReadOnlyList<ProtoFeedbackChannelResult> Channels)
{
    /// <summary>True when a channel failed; the CLI exits 1 so a broken post is not silent.</summary>
    public bool Failed { get; } = Channels.Any(channel => channel.Status == ProtoFeedbackStatuses.Failed);
}

/// <summary>
/// Where the channels post: the GitHub pull request the digest is about and the webhook address. A
/// missing piece is a named skip, never a guess. The trace link is the CI artifact URL the action
/// composed, because a local archive has no viewer URL.
/// </summary>
public sealed record ProtoFeedbackTarget
{
    /// <summary>The GitHub token for the comment; <c>GITHUB_TOKEN</c> in the action.</summary>
    public string? Token { get; init; }

    /// <summary>The repository the comment is posted to, as <c>owner/name</c>.</summary>
    public string? Repository { get; init; }

    /// <summary>The pull request number the digest is about, read from the event payload.</summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>
    /// True when the pull request comes from a fork. Its workflow token can read but not comment, so the
    /// comment channel skips with that reason; the annotations and the job summary still carry the evidence.
    /// </summary>
    public bool FromFork { get; init; }

    /// <summary>The GitHub REST base URL; defaults to <c>https://api.github.com</c>.</summary>
    public Uri? ApiUrl { get; init; }

    /// <summary>The CI artifact URL the comment links to; null prints the archive reference instead.</summary>
    public string? TraceLink { get; init; }

    /// <summary>The webhook address the digest JSON is posted to.</summary>
    public Uri? WebhookUrl { get; init; }

    /// <summary>The shared-secret header value; no header is sent without it.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>The shared-secret header name; defaults to <c>X-ProtoTest-Secret</c>.</summary>
    public string? WebhookSecretHeader { get; init; }

    /// <summary>
    /// The run compared with the base branch's last green run. The comment then says which tests the
    /// change broke or fixed and where each one left the baseline; null leaves that section out.
    /// </summary>
    public ProtoTraceComparison? Comparison { get; init; }

    /// <summary>
    /// The verdict over the reports the base branch's run and this run embedded. The comment then shows
    /// the coverage that moved, the units the change added without a test and the ones it stopped
    /// covering; null leaves that section out.
    /// </summary>
    public ProtoVerificationVerdict? Coverage { get; init; }

    /// <summary>Where to cover each uncovered unit, from this run; the comment names it next to a new gap.</summary>
    public IReadOnlyList<ProtoCoverageSuggestion> CoverageSuggestions { get; init; } = [];

    /// <summary>
    /// The summary card image the comment opens with, drawn from the run's counts and its coverage category
    /// names only; null leaves the card out. The CLI sets <c>https://api.prototest.dev/evidence/card.svg</c>.
    /// </summary>
    public Uri? SummaryCardUrl { get; init; }

    /// <summary>
    /// The trace viewer the comment points to beside <see cref="TraceLink"/>: the downloaded artifact opens
    /// there, in the reader's browser. Null leaves the pointer out. The CLI sets <c>https://trace.prototest.dev/</c>.
    /// </summary>
    public Uri? ViewerUrl { get; init; }

    /// <summary>
    /// The address a relative source location links under, such as
    /// <c>https://github.com/owner/repo/blob/{sha}/</c>; null prints the location without a link.
    /// </summary>
    public Uri? SourceBaseUrl { get; init; }
}

/// <summary>
/// Builds the digest one feedback post carries and posts it through the channels. The digest is the
/// diagnosis document: run identity, outcome counts, each non-succeeded test's failure, the gates,
/// the findings and the coverage the embedded report published. The channels render it; nothing here
/// builds a second summary.
/// </summary>
public static class ProtoFeedback
{
    /// <summary>Reads the digest from a trace file.</summary>
    public static ProtoDiagnosisDocument ReadDigest(string tracePath) => ProtoDiagnosis.Read(tracePath);

    /// <summary>Reads the digest from an opened archive.</summary>
    public static ProtoDiagnosisDocument ReadDigest(ProtoTraceArchive archive) => ProtoDiagnosis.Read(archive);

    /// <summary>The digest as its one JSON document.</summary>
    public static string DigestJson(ProtoDiagnosisDocument digest) => ProtoDiagnosisJson.ToJson(digest);

    /// <summary>
    /// Runs every channel in order: the workflow annotations into <paramref name="annotations"/>, the
    /// pull request comment and the webhook over the client. Channels are independent; every result is
    /// reported.
    /// </summary>
    public static async Task<ProtoFeedbackReport> PostAsync(
        ProtoDiagnosisDocument digest,
        ProtoFeedbackTarget target,
        HttpClient client,
        TextWriter annotations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(annotations);

        var results = new List<ProtoFeedbackChannelResult>
        {
            ProtoFeedbackAnnotations.Write(digest, annotations),
            await ProtoFeedbackComment.PostAsync(digest, target, client, cancellationToken).ConfigureAwait(false),
            await ProtoFeedbackWebhook.PostAsync(digest, target, client, cancellationToken).ConfigureAwait(false)
        };

        return new ProtoFeedbackReport(results);
    }
}
