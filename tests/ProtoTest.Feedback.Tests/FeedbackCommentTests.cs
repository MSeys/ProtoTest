namespace ProtoTest.Feedback.Tests;

using System.Text.Json;

/// <summary>
/// The github-pr-comment channel: the Markdown rendering of the digest and the REST post to the pull
/// request the event payload named.
/// </summary>
[TestFixture]
public sealed class FeedbackCommentTests
{
    [Test]
    public void Markdown_ShouldRenderTheDigestDeterministically()
    {
        var markdown = ProtoFeedbackComment.Markdown(
            FeedbackFixtures.FailedDigest(), "https://example.test/artifact");

        var expected = string.Join(Environment.NewLine,
        [
            "## ProtoTest run `run-1`",
            string.Empty,
            "**1 tests · 1 failed**",
            string.Empty,
            "- **FAILED `orders match their shape`** (16 ms)",
            "  - `assert.json.shape` · failed",
            "  - Shape mismatch failed with 1 error(s).",
            "  - at `tests/Orders/OrderTests.cs:42`",
            "  - mismatch `$.orderId`: expected 7, actual 42",
            string.Empty,
            "Coverage: 12/20 (60%)",
            string.Empty,
            "[Full trace](https://example.test/artifact)"
        ]) + Environment.NewLine;

        Assert.That(markdown, Is.EqualTo(expected));
    }

    [Test]
    public void Markdown_ShouldCarryTheTraceReferenceWithoutALink()
    {
        var markdown = ProtoFeedbackComment.Markdown(FeedbackFixtures.FailedDigest());

        Assert.That(markdown, Does.Contain("Trace: `TestResults/run-1.prototrace`"));
        Assert.That(markdown, Does.Not.Contain("[Full trace]"));
    }

    [Test]
    public void Markdown_ShouldListFailedGates()
    {
        var markdown = ProtoFeedbackComment.Markdown(FeedbackFixtures.FailedDigest(failedGate: true));

        Assert.That(markdown, Does.Contain("**Run gates**"));
        Assert.That(
            markdown,
            Does.Contain("- `coverage gate` failed: Coverage regressed below the agreed floor."));
    }

    [Test]
    public async Task PostAsync_ShouldPostTheCommentToThePullRequest()
    {
        await using var server = RecordingHttpServer.Start(201);
        using var client = new HttpClient();
        var target = new ProtoFeedbackTarget
        {
            Token = "test-token",
            Repository = "owner/repo",
            PullRequestNumber = 7,
            ApiUrl = new Uri(server.Url),
            TraceLink = "https://example.test/artifact"
        };

        var result = await ProtoFeedbackComment.PostAsync(FeedbackFixtures.FailedDigest(), target, client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Channel, Is.EqualTo(ProtoFeedbackChannels.GithubPrComment));
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(result.Reason, Is.Null);
        }

        var request = server.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/repos/owner/repo/issues/7/comments"));
            Assert.That(request.Headers["Authorization"], Is.EqualTo("Bearer test-token"));
            Assert.That(request.Headers["X-GitHub-Api-Version"], Is.EqualTo("2022-11-28"));
        }

        using var body = JsonDocument.Parse(request.Body);
        Assert.That(
            body.RootElement.GetProperty("body").GetString(),
            Does.Contain("[Full trace](https://example.test/artifact)"));
    }

    [Test]
    public async Task PostAsync_ShouldFailWhenTheApiRefuses()
    {
        await using var server = RecordingHttpServer.Start(500);
        using var client = new HttpClient();

        var result = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget
            {
                Token = "test-token",
                Repository = "owner/repo",
                PullRequestNumber = 7,
                ApiUrl = new Uri(server.Url)
            },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Failed));
            Assert.That(result.Reason, Does.Contain("500"));
        }
    }

    [Test]
    public async Task PostAsync_ShouldSkipAMissingTargetWithItsReason()
    {
        using var client = new HttpClient();

        var noToken = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget { Repository = "owner/repo", PullRequestNumber = 7 },
            client);
        var noRepository = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget { Token = "test-token", PullRequestNumber = 7 },
            client);
        var noPullRequest = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget { Token = "test-token", Repository = "owner/repo" },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noToken.Status, Is.EqualTo(ProtoFeedbackStatuses.Skipped));
            Assert.That(noToken.Reason, Does.Contain("GITHUB_TOKEN"));
            Assert.That(noRepository.Status, Is.EqualTo(ProtoFeedbackStatuses.Skipped));
            Assert.That(noRepository.Reason, Does.Contain("GITHUB_REPOSITORY"));
            Assert.That(noPullRequest.Status, Is.EqualTo(ProtoFeedbackStatuses.Skipped));
            Assert.That(noPullRequest.Reason, Does.Contain("GITHUB_EVENT_PATH"));
        }
    }

    [Test]
    public async Task PostAsync_ShouldSkipAGreenRun()
    {
        await using var server = RecordingHttpServer.Start(201);
        using var client = new HttpClient();

        var result = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.GreenDigest(),
            new ProtoFeedbackTarget
            {
                Token = "test-token",
                Repository = "owner/repo",
                PullRequestNumber = 7,
                ApiUrl = new Uri(server.Url)
            },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Skipped));
            Assert.That(result.Reason, Does.Contain("no failures to report"));
            Assert.That(server.Requests, Is.Empty);
        }
    }
}
