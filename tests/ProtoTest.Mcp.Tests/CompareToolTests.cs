namespace ProtoTest.Mcp.Tests;

using System.Text.Json;
using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// The `compare_runs` tool through a real MCP session: the newest run against the one before it, an
/// explicit baseline trace, and the named error when there is nothing older to compare with.
/// </summary>
[TestFixture]
public sealed class CompareToolTests
{
    [Test]
    public async Task CompareRuns_ShouldCompareTheNewestRunWithTheRunBeforeIt()
    {
        using var folder = new TestProjectFolder();
        var older = folder.AddFixture("run-passed");
        var newer = folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var comparison = await session.CallJsonAsync("compare_runs");
        var test = comparison.GetProperty("tests")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(comparison.GetProperty("baseline").GetProperty("runId").GetString(), Is.EqualTo(TestProjectFolder.RunId(older)));
            Assert.That(comparison.GetProperty("current").GetProperty("runId").GetString(), Is.EqualTo(TestProjectFolder.RunId(newer)));
            Assert.That(comparison.GetProperty("counts").GetProperty("unchanged").GetInt32(), Is.EqualTo(1));
            Assert.That(comparison.GetProperty("hasBroken").GetBoolean(), Is.False);
            Assert.That(comparison.GetProperty("tests").GetArrayLength(), Is.EqualTo(1), "unchanged tests are counted, not listed");
            Assert.That(test.GetProperty("name").GetString(), Is.EqualTo("orders match their shape"));
            Assert.That(test.GetProperty("change").GetString(), Is.EqualTo("new"));
        }
    }

    [Test]
    public async Task CompareRuns_ShouldUseABaselineTraceAndNameWhereTheTestBroke()
    {
        using var folder = new TestProjectFolder();
        using var baseline = new TemporaryTrace("mcp-compare-baseline");
        await WriteAsync(baseline.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));
        await WriteAsync(
            Path.Combine(folder.TestResults, "current.prototrace"),
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer in 2 seconds."))));
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var comparison = await session.CallJsonAsync("compare_runs", new Dictionary<string, object?> { ["baselineTrace"] = baseline.Path });
        var test = comparison.GetProperty("tests")[0];
        var divergence = test.GetProperty("divergence");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(comparison.GetProperty("hasBroken").GetBoolean(), Is.True);
            Assert.That(test.GetProperty("change").GetString(), Is.EqualTo("broken"));
            Assert.That(divergence.GetProperty("reason").GetString(), Is.EqualTo("status-changed"));
            Assert.That(divergence.GetProperty("current").GetProperty("subject").GetString(), Is.EqualTo("GET /orders"));
            Assert.That(divergence.GetProperty("current").GetProperty("errorMessage").GetString(), Does.Contain("No answer"));
            Assert.That(divergence.GetProperty("baseline").GetProperty("status").GetString(), Is.EqualTo("succeeded"));
        }
    }

    [Test]
    public async Task CompareRuns_ShouldNameTheMissingBaseline()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var alone = await session.CallToolAsync("compare_runs");
        var both = await session.CallToolAsync("compare_runs", new Dictionary<string, object?>
        {
            ["baselineRunId"] = "x",
            ["baselineTrace"] = "y.prototrace"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alone.IsError, Is.True);
            Assert.That(McpSession.Text(alone), Does.Contain("no older run"));
            Assert.That(both.IsError, Is.True);
            Assert.That(McpSession.Text(both), Does.Contain("not both"));
        }
    }

    [Test]
    public async Task CompareRuns_ShouldReturnJsonWithinTheTokenBudget()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-passed");
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var result = await session.CallToolAsync("compare_runs");

        Assert.That(McpSession.Text(result).Length, Is.LessThan(4000));
        Assert.That(() => JsonDocument.Parse(McpSession.Text(result)), Throws.Nothing);
    }
}
