namespace ProtoTest.Mcp.Tests;

using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>The `review_tests` tool through a real MCP session: findings with next steps, clean tests counted.</summary>
[TestFixture]
public sealed class ReviewToolTests
{
    [Test]
    public async Task ReviewTests_ShouldReturnFindingsWithTheirNextStep()
    {
        using var folder = new TestProjectFolder();
        await WriteAsync(
            Path.Combine(folder.TestResults, "run.prototrace"),
            new RecordedTest("orders are listed", Call("List orders", "GET /orders")),
            new RecordedTest("invoices are paid", Call("Pay invoice", "POST /invoices"), Check("paid")));
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var review = await session.CallJsonAsync("review_tests");
        var test = review.GetProperty("tests")[0];
        var finding = test.GetProperty("findings")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(review.GetProperty("reviewed").GetInt32(), Is.EqualTo(2));
            Assert.That(review.GetProperty("clean").GetInt32(), Is.EqualTo(1));
            Assert.That(review.GetProperty("counts").GetProperty("no-check").GetInt32(), Is.EqualTo(1));
            Assert.That(test.GetProperty("name").GetString(), Is.EqualTo("orders are listed"));
            Assert.That(finding.GetProperty("rule").GetString(), Is.EqualTo("no-check"));
            Assert.That(finding.GetProperty("next").GetString(), Does.Contain("Should.HaveStatus"));
            Assert.That(finding.GetProperty("subject").GetString(), Is.EqualTo("GET /orders"));
        }
    }

    [Test]
    public async Task ReviewTests_ShouldReviewOnlyTheNamedTests()
    {
        using var folder = new TestProjectFolder();
        await WriteAsync(
            Path.Combine(folder.TestResults, "run.prototrace"),
            new RecordedTest("orders are listed", Call("List orders", "GET /orders")),
            new RecordedTest("invoices are paid", Call("Pay invoice", "POST /invoices"), Check("paid")));
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var review = await session.CallJsonAsync("review_tests", new Dictionary<string, object?> { ["tests"] = new[] { "invoices are paid" } });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(review.GetProperty("reviewed").GetInt32(), Is.EqualTo(1));
            Assert.That(review.GetProperty("tests").GetArrayLength(), Is.Zero);
        }
    }
}
