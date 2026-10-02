namespace ProtoTest.Feedback.Tests;

using System.Text.Json;
using ProtoTest.Cli;
using ProtoTest.TestSupport;
using ProtoTest.Verification;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// The pull request comment with a comparison against the base branch: the tests a change broke and
/// fixed, a green run that fixed a test still posting, and the CLI's <c>--baseline</c> option.
/// </summary>
[TestFixture]
public sealed class FeedbackComparisonTests
{
    [Test]
    public async Task Markdown_ShouldListBrokenThenFixedTestsWithWhereTheyLeftTheBaseline()
    {
        var comparison = await CompareAsync(
            [
                new RecordedTest("orders are listed", Call("List orders", "GET /orders")),
                new RecordedTest("invoices are paid", FailedCall("Pay invoice", "POST /invoices", new TimeoutException("No answer.")))
            ],
            [
                new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer."))),
                new RecordedTest("invoices are paid", Call("Pay invoice", "POST /invoices"))
            ]);

        var markdown = ProtoFeedbackComment.Markdown(FeedbackFixtures.FailedDigest(), null, comparison);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markdown, Does.Contain("**Compared with the base branch**"));
            Assert.That(markdown, Does.Contain("- broke `orders are listed` at `http.request` `GET /orders`"));
            Assert.That(markdown, Does.Contain("- fixed `invoices are paid` at `http.request` `POST /invoices`"));
            Assert.That(markdown.IndexOf("- broke", StringComparison.Ordinal), Is.LessThan(markdown.IndexOf("- fixed", StringComparison.Ordinal)));
        }
    }

    [Test]
    public async Task Markdown_ShouldSayWhenNoTestChangedOutcome()
    {
        var same = new RecordedTest("orders are listed", Call("List orders", "GET /orders"));
        var comparison = await CompareAsync([same], [same]);

        var markdown = ProtoFeedbackComment.Markdown(FeedbackFixtures.FailedDigest(), null, comparison);

        Assert.That(markdown, Does.Contain("no test changed outcome"));
    }

    [Test]
    public async Task PostAsync_ShouldPostAGreenRunThatFixedATest()
    {
        var comparison = await CompareAsync(
            [new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer.")))],
            [new RecordedTest("orders are listed", Call("List orders", "GET /orders"))]);
        await using var server = RecordingHttpServer.Start(201);
        using var client = new HttpClient();

        var result = await ProtoFeedbackComment.PostAsync(
            FeedbackFixtures.GreenDigest(),
            new ProtoFeedbackTarget
            {
                Token = "test-token",
                Repository = "owner/repo",
                PullRequestNumber = 7,
                ApiUrl = new Uri(server.Url),
                Comparison = comparison
            },
            client);

        using var body = JsonDocument.Parse(server.Requests.Single().Body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(body.RootElement.GetProperty("body").GetString(), Does.Contain("- fixed `orders are listed`"));
        }
    }

    [Test]
    public void Cli_ShouldNameAMissingBaselineTrace()
    {
        var trace = FeedbackFixtures.McpFixture("run-failed");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = FeedbackFixtures.WithEnvironmentResult(
            FeedbackFixtures.TargetEnvironmentVariables.ToDictionary(name => name, _ => (string?)null),
            () => CliHost.Run(["feedback", trace, "--baseline", "missing.prototrace"], output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("Baseline trace not found: missing.prototrace"));
        }
    }

    private static async Task<ProtoTraceComparison> CompareAsync(RecordedTest[] baseline, RecordedTest[] current)
    {
        using var before = new TemporaryTrace("feedback-baseline");
        using var after = new TemporaryTrace("feedback-current");
        await WriteAsync(before.Path, baseline);
        await WriteAsync(after.Path, current);
        return ProtoVerification.Compare(before.Path, after.Path);
    }
}
