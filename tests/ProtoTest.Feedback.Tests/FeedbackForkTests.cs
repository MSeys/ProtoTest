namespace ProtoTest.Feedback.Tests;

using ProtoTest.Cli;

/// <summary>
/// A pull request from a fork runs with a token that cannot comment: the comment channel skips with
/// that reason instead of failing the step, while a pull request from the repository itself still posts,
/// and a refused post in the repository names the missing permission.
/// </summary>
[TestFixture]
// The CLI reads its targets from process-wide environment variables, so these tests run alone.
[NonParallelizable]
public sealed class FeedbackForkTests
{
    private const string ForkEvent =
        """{"pull_request":{"number":7,"head":{"repo":{"full_name":"someone/repo"}},"base":{"repo":{"full_name":"owner/repo"}}}}""";

    private const string SameRepositoryEvent =
        """{"pull_request":{"number":7,"head":{"repo":{"full_name":"owner/repo"}},"base":{"repo":{"full_name":"owner/repo"}}}}""";

    [Test]
    public async Task Feedback_ShouldSkipTheCommentOnAPullRequestFromAFork()
    {
        var (exit, error, server) = await RunAsync(ForkEvent, status: 403);
        await using var _ = server;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(0), "a fork's read-only token must not fail the step");
            Assert.That(error, Does.Contain("github-pr-comment skipped (The pull request comes from a fork"));
            Assert.That(server.Requests, Is.Empty, "nothing is sent that the token cannot do");
        }
    }

    [Test]
    public async Task Feedback_ShouldPostOnAPullRequestFromTheRepositoryItself()
    {
        var (exit, error, server) = await RunAsync(SameRepositoryEvent, status: 201);
        await using var _ = server;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Does.Contain("github-pr-comment posted"));
            Assert.That(server.Requests.Any(request => request.Method == "POST"), Is.True);
        }
    }

    [Test]
    public async Task Feedback_ShouldNameTheMissingPermissionWhenGitHubRefusesInTheRepository()
    {
        var (exit, error, server) = await RunAsync(SameRepositoryEvent, status: 403);
        await using var _ = server;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Does.Contain("github-pr-comment failed"));
            Assert.That(error, Does.Contain("pull-requests: write"));
        }
    }

    private static async Task<(int Exit, string Error, RecordingHttpServer Server)> RunAsync(string payload, int status)
    {
        var directory = FeedbackFixtures.NewTempDirectory("feedback-fork");
        var server = RecordingHttpServer.Start(status);
        try
        {
            var payloadPath = Path.Combine(directory, "event.json");
            await File.WriteAllTextAsync(payloadPath, payload);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = 0;
            FeedbackFixtures.WithEnvironment(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GITHUB_TOKEN"] = "test-token",
                ["GITHUB_REPOSITORY"] = "owner/repo",
                ["GITHUB_EVENT_PATH"] = payloadPath,
                ["GITHUB_API_URL"] = server.Url
            }, () => exit = CliHost.Run(["feedback", FeedbackFixtures.McpFixture("run-failed")], output, error));
            return (exit, error.ToString(), server);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
