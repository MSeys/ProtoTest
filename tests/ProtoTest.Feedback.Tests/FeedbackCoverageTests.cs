namespace ProtoTest.Feedback.Tests;

using System.Text.Json;
using ProtoTest.Cli;
using ProtoTest.Diagnosis;
using ProtoTest.Verification;

/// <summary>
/// The pull request comment's coverage section and the one comment a pull request keeps: a unit the
/// change added without a test, one it stopped covering, an update instead of a second comment, and
/// <c>verify --strict</c>.
/// </summary>
[TestFixture]
public sealed class FeedbackCoverageTests
{
    private const string ExistingComments = """[{"id":7,"body":"Thanks!"},{"id":42,"body":"<!-- prototest-evidence -->\n## ProtoTest run `old`"}]""";

    [Test]
    public void Markdown_ShouldNameNewUncoveredUnitsWithWhereToCoverThem()
    {
        var verdict = AddedEndpointVerdict(out _);
        ProtoCoverageSuggestion[] suggestions =
        [
            new("Shop:Api", "OpenAPI", "DELETE /orders/{id}", "DELETE /orders/{id}", ProtoCoverageActions.New, "an order is read", "tests/OrderTests.cs",
                "No test calls DELETE /orders/{id}. 'an order is read' calls GET /orders/42 on the same path; write a new test shaped like it.")
        ];

        var markdown = ProtoFeedbackComment.Markdown(FeedbackFixtures.GreenDigest(), null, null, verdict, suggestions);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markdown, Does.StartWith(ProtoFeedbackComment.Marker));
            Assert.That(markdown, Does.Contain("**Coverage against the base branch**"));
            Assert.That(markdown, Does.Contain("- `Shop:Api` · OpenAPI: 1/1 → 1/2 (-50 points)"));
            Assert.That(markdown, Does.Contain("- new and uncovered: `DELETE /orders/{id}` (Shop:Api · OpenAPI). No test calls DELETE /orders/{id}."));
            Assert.That(markdown, Does.Contain("write a new test shaped like it."));
        }
    }

    [Test]
    public void Markdown_ShouldNameAUnitTheChangeStoppedCovering()
    {
        var directory = FeedbackFixtures.NewTempDirectory("coverage-regressed");
        try
        {
            var (baseline, current) = FeedbackFixtures.RegressedReports(directory);
            var verdict = ProtoVerification.Verify(ProtoVerificationRun.FromReportFile(baseline), ProtoVerificationRun.FromReportFile(current));

            var markdown = ProtoFeedbackComment.Markdown(FeedbackFixtures.GreenDigest(), null, null, verdict);

            Assert.That(markdown, Does.Contain("- no longer covered: `GET /api/v1/orders` (Northstar:Api · OpenAPI)"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task PostAsync_ShouldPostAGreenRunThatAddedAnUncoveredUnit()
    {
        await using var server = RecordingHttpServer.Start(201);
        using var client = new HttpClient();

        var result = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.GreenDigest(),
            Target(server) with { Coverage = AddedEndpointVerdict(out _) },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(server.Requests.Select(request => request.Method), Is.EqualTo(new[] { "GET", "POST" }));
        }
    }

    [Test]
    public async Task PostAsync_ShouldUpdateTheCommentAnEarlierRunPosted()
    {
        await using var server = RecordingHttpServer.Start(201, request =>
            request.Method == "GET" ? (200, ExistingComments) : null);
        using var client = new HttpClient();

        var result = await ProtoFeedbackComment.PostAsync(FeedbackFixtures.FailedDigest(), Target(server), client);

        var update = server.Requests.Single(request => request.Method == "PATCH");
        using var body = JsonDocument.Parse(update.Body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(result.Reason, Is.EqualTo("Updated comment 42."));
            Assert.That(update.Path, Is.EqualTo("/repos/owner/repo/issues/comments/42"));
            Assert.That(server.Requests.Any(request => request.Method == "POST"), Is.False, "one comment per pull request");
            Assert.That(body.RootElement.GetProperty("body").GetString(), Does.Contain("FAILED"));
        }
    }

    [Test]
    public async Task PostAsync_ShouldTurnAnEarlierCommentGreenWhenTheRunIs()
    {
        await using var server = RecordingHttpServer.Start(201, request =>
            request.Method == "GET" ? (200, ExistingComments) : null);
        using var client = new HttpClient();

        var result = await ProtoFeedbackComment.PostAsync(FeedbackFixtures.GreenDigest(), Target(server), client);

        var update = server.Requests.Single(request => request.Method == "PATCH");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(JsonDocument.Parse(update.Body).RootElement.GetProperty("body").GetString(), Does.Contain("succeeded"));
        }
    }

    [Test]
    public void VerifyStrict_ShouldFailAUnitTheChangeAddedWithoutATest()
    {
        var directory = FeedbackFixtures.NewTempDirectory("verify-strict");
        try
        {
            var baseline = FeedbackFixtures.WriteReport(Path.Combine(directory, "baseline.json"),
                FeedbackFixtures.Unit("Shop:Api", "OpenAPI", "GET /orders", covered: true));
            var current = FeedbackFixtures.WriteReport(Path.Combine(directory, "current.json"),
                FeedbackFixtures.Unit("Shop:Api", "OpenAPI", "GET /orders", covered: true),
                FeedbackFixtures.Unit("Shop:Api", "OpenAPI", "DELETE /orders/{id}", covered: false));
            using var output = new StringWriter();
            using var error = new StringWriter();

            var lenient = CliHost.Run(["verify", baseline, current], new StringWriter(), new StringWriter());
            var strict = CliHost.Run(["verify", baseline, current, "--strict"], output, error);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lenient, Is.EqualTo(0), "a new uncovered unit warns by default");
                Assert.That(strict, Is.EqualTo(1));
                Assert.That(output.ToString(), Does.Contain("::error::added-uncovered: "));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProtoFeedbackTarget Target(RecordingHttpServer server) => new()
    {
        Token = "test-token",
        Repository = "owner/repo",
        PullRequestNumber = 7,
        ApiUrl = new Uri(server.Url)
    };

    private static ProtoVerificationVerdict AddedEndpointVerdict(out string directory)
    {
        directory = FeedbackFixtures.NewTempDirectory("coverage-added");
        var baseline = FeedbackFixtures.WriteReport(Path.Combine(directory, "baseline.json"),
            FeedbackFixtures.Unit("Shop:Api", "OpenAPI", "GET /orders", covered: true));
        var current = FeedbackFixtures.WriteReport(Path.Combine(directory, "current.json"),
            FeedbackFixtures.Unit("Shop:Api", "OpenAPI", "GET /orders", covered: true),
            FeedbackFixtures.Unit("Shop:Api", "OpenAPI", "DELETE /orders/{id}", covered: false));
        var verdict = ProtoVerification.Verify(ProtoVerificationRun.FromReportFile(baseline), ProtoVerificationRun.FromReportFile(current));
        Directory.Delete(directory, recursive: true);
        return verdict;
    }
}
