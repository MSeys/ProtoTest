namespace ProtoTest.Feedback.Tests;

using System.Text.Json;
using ProtoTest.Cli;

/// <summary>
/// The `prototest feedback` verb end to end: arguments, environment targets, the digest file and the
/// per-channel outcomes. The fixture drives the real command with the real channels.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class FeedbackCliTests
{
    [Test]
    public void Feedback_ShouldWriteTheDigestAndSkipTheMissingTargets()
    {
        var directory = FeedbackFixtures.NewTempDirectory("feedback-cli");
        try
        {
            var digestPath = Path.Combine(directory, "digest.json");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = 0;
            FeedbackFixtures.WithEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal), () =>
                exit = CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed"), "--digest", digestPath], output, error));

            var expected = ProtoFeedback.DigestJson(ProtoFeedback.ReadDigest(FeedbackFixtures.McpFixture("run-failed")));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(File.ReadAllText(digestPath), Is.EqualTo(expected));
                Assert.That(output.ToString(), Does.StartWith("::error file="));
                Assert.That(output.ToString(), Does.Contain("orders match their shape"));
                Assert.That(error.ToString(), Does.Contain("github-pr-comment skipped (No GitHub token"));
                Assert.That(error.ToString(), Does.Contain("webhook skipped (No webhook URL"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Feedback_ShouldPostTheCommentToTheEventPullRequest()
    {
        var directory = FeedbackFixtures.NewTempDirectory("feedback-cli");
        await using var server = RecordingHttpServer.Start(201);
        try
        {
            var payloadPath = Path.Combine(directory, "event.json");
            await File.WriteAllTextAsync(payloadPath, """{"pull_request":{"number":7}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = 0;
            FeedbackFixtures.WithEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GITHUB_TOKEN"] = "test-token",
                ["GITHUB_REPOSITORY"] = "owner/repo",
                ["GITHUB_EVENT_PATH"] = payloadPath,
                ["GITHUB_API_URL"] = server.Url,
                ["PROTOTEST_FEEDBACK_TRACE_URL"] = "https://example.test/artifact"
            }, () => exit = CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed")], output, error));

            // The first request looks for an earlier ProtoTest comment to update; the second posts.
            var request = server.Requests.Single(candidate => candidate.Method == "POST");
            using var body = JsonDocument.Parse(request.Body);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(request.Path, Is.EqualTo("/repos/owner/repo/issues/7/comments"));
                Assert.That(request.Headers["Authorization"], Is.EqualTo("Bearer test-token"));
                Assert.That(
                    body.RootElement.GetProperty("body").GetString(),
                    Does.Contain("[**Open the full trace ↗**](https://example.test/artifact) (download it and drop it on [ProtoTrace](https://trace.prototest.dev/))"),
                    "the artifact opens in the viewer, which the CLI points to by default");
                Assert.That(error.ToString(), Does.Contain("github-pr-comment posted"));
                Assert.That(error.ToString(), Does.Contain("webhook skipped"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Feedback_ShouldOpenTheCommentWithTheSummaryCardByDefault()
    {
        var body = await PostedBodyAsync(new Dictionary<string, string?>(StringComparer.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain("<img src=\"https://api.prototest.dev/evidence/card.svg?broke="));
            Assert.That(body, Does.Contain("&amp;theme=dark"));
            Assert.That(body, Does.Contain("](https://github.com/owner/repo/blob/0123abc/"), "source locations link to the head commit");
        }
    }

    [Test]
    public async Task Feedback_ShouldLeaveTheCardOutWhenItIsTurnedOff()
    {
        var off = await PostedBodyAsync(new Dictionary<string, string?>(StringComparer.Ordinal) { ["PROTOTEST_FEEDBACK_CARD_URL"] = "off" });
        var own = await PostedBodyAsync(new Dictionary<string, string?>(StringComparer.Ordinal) { ["PROTOTEST_FEEDBACK_CARD_URL"] = "https://cards.example.test/card.svg" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(off, Does.Not.Contain("<picture>"));
            Assert.That(off, Does.Not.Contain("api.prototest.dev"));
            Assert.That(own, Does.Contain("<img src=\"https://cards.example.test/card.svg?broke="));
        }
    }

    [Test]
    public void Feedback_ShouldFailAnInvalidCardAddress()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = FeedbackFixtures.WithEnvironmentResult(
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["PROTOTEST_FEEDBACK_CARD_URL"] = "cards" },
            () => CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed")], output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("not an absolute URL for PROTOTEST_FEEDBACK_CARD_URL"));
        }
    }

    [Test]
    public void Feedback_ShouldPostTheWebhookForAGreenRun()
    {
        using var server = RecordingHttpServer.Start(200);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = 0;
        FeedbackFixtures.WithEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PROTOTEST_FEEDBACK_WEBHOOK_URL"] = server.Url,
            ["PROTOTEST_FEEDBACK_WEBHOOK_SECRET"] = "shared-secret"
        }, () => exit = CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-passed")], output, error));

        var request = server.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(output.ToString(), Is.Empty, "A green run has nothing to annotate.");
            Assert.That(request.Headers[ProtoFeedbackWebhook.DefaultSecretHeader], Is.EqualTo("shared-secret"));
            Assert.That(request.Body, Does.Contain("\"digestVersion\":\"1\""));
            Assert.That(error.ToString(), Does.Contain("github-annotations skipped"));
            Assert.That(error.ToString(), Does.Contain("github-pr-comment skipped"));
            Assert.That(error.ToString(), Does.Contain("webhook posted"));
        }
    }

    [Test]
    public void Feedback_ShouldFailAChannelThatTheEndpointRefuses()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = 0;
        FeedbackFixtures.WithEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PROTOTEST_FEEDBACK_WEBHOOK_URL"] = "http://127.0.0.1:1/"
        }, () => exit = CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed")], output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("webhook failed"));
        }
    }

    [Test]
    public void Feedback_ShouldFailAConfiguredButInvalidTarget()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = FeedbackFixtures.WithEnvironmentResult(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PROTOTEST_FEEDBACK_WEBHOOK_URL"] = "not a url"
            },
            () => CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed")], output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("not an absolute URL for PROTOTEST_FEEDBACK_WEBHOOK_URL"));
        }
    }

    [Test]
    public void Feedback_ShouldNameAMissingTrace()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = FeedbackFixtures.WithEnvironmentResult(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            () => CliHost.Run(["feedback", "does-not-exist.prototrace"], output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("does-not-exist.prototrace"));
        }
    }

    [Test]
    public void Feedback_ShouldPrintUsageForAnUnknownOption()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = FeedbackFixtures.WithEnvironmentResult(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            () => CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed"), "--unknown"], output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("prototest feedback <file.prototrace>"));
        }
    }

    // Runs the feedback verb against the base branch's passing run and returns the comment body it posted.
    private static async Task<string> PostedBodyAsync(Dictionary<string, string?> environment)
    {
        var directory = FeedbackFixtures.NewTempDirectory("feedback-cli-card");
        await using var server = RecordingHttpServer.Start(201);
        try
        {
            var payloadPath = Path.Combine(directory, "event.json");
            await File.WriteAllTextAsync(payloadPath, """{"pull_request":{"number":7,"head":{"sha":"0123abc"}}}""");
            environment["GITHUB_TOKEN"] = "test-token";
            environment["GITHUB_REPOSITORY"] = "owner/repo";
            environment["GITHUB_EVENT_PATH"] = payloadPath;
            environment["GITHUB_API_URL"] = server.Url;
            environment["GITHUB_SHA"] = "merge-commit";
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = FeedbackFixtures.WithEnvironmentResult(environment, () => CliHost.Run(
                ["feedback", FeedbackFixtures.McpFixture("run-failed"), "--baseline", FeedbackFixtures.McpFixture("run-passed")],
                output,
                error));

            Assert.That(exit, Is.EqualTo(0), error.ToString());
            using var body = JsonDocument.Parse(server.Requests.Single(candidate => candidate.Method == "POST").Body);
            return body.RootElement.GetProperty("body").GetString()!;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
