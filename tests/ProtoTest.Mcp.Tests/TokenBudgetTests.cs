namespace ProtoTest.Mcp.Tests;

/// <summary>
/// Token discipline is acceptance: the tools cap lists, messages and pages, so an oversized run stays
/// a bounded payload instead of a whole trace.
/// </summary>
[TestFixture]
public sealed class TokenBudgetTests
{
    [Test]
    public async Task ToolOutput_ShouldStayBoundedOnAnOversizedRun()
    {
        using var folder = new TestProjectFolder();
        await NoisyTrace.WriteAsync(folder.Path, uncoveredUnits: 1000, messageCharacters: 200_000);
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var listed = await session.CallToolAsync("list_runs");
        var failure = await session.CallToolAsync("get_failure");
        var coverage = await session.CallToolAsync("get_coverage");
        var diagnosis = await session.CallToolAsync("get_diagnosis");
        var context = await session.CallToolAsync("get_diagnosis", new Dictionary<string, object?> { ["detail"] = "context" });
        var pagedToTheCap = await session.CallToolAsync("get_coverage", new Dictionary<string, object?>
        {
            ["includeUncovered"] = true,
            ["limit"] = 1000
        });
        var suiteMap = await session.CallToolAsync("get_suite_map");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.IsError, Is.Not.True);
            Assert.That(failure.IsError, Is.Not.True);
            Assert.That(coverage.IsError, Is.Not.True);
            Assert.That(diagnosis.IsError, Is.Not.True);
            Assert.That(context.IsError, Is.Not.True);
            Assert.That(McpSession.Text(listed).Length, Is.LessThan(16 * 1024));
            Assert.That(McpSession.Text(failure).Length, Is.LessThan(16 * 1024));
            Assert.That(McpSession.Text(coverage).Length, Is.LessThan(16 * 1024));
            Assert.That(McpSession.Text(diagnosis).Length, Is.LessThan(16 * 1024));
            Assert.That(McpSession.Text(context).Length, Is.LessThan(32 * 1024));
            Assert.That(McpSession.Text(pagedToTheCap).Length, Is.LessThan(32 * 1024));
            Assert.That(suiteMap.IsError, Is.Not.True);
            Assert.That(McpSession.Text(suiteMap).Length, Is.LessThan(16 * 1024));
        }

        var failureJson = McpSession.Json(failure);
        var coverageJson = McpSession.Json(coverage);
        var diagnosisJson = McpSession.Json(diagnosis);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                failureJson.GetProperty("failure").GetProperty("errorMessage").GetString(),
                Does.Contain("truncated"));
            Assert.That(
                diagnosisJson.GetProperty("failures")[0].GetProperty("failure").GetProperty("errorMessage").GetString(),
                Does.Contain("truncated"));
            Assert.That(coverageJson.GetProperty("uncovered").GetArrayLength(), Is.EqualTo(50));
            Assert.That(coverageJson.GetProperty("uncoveredTotal").GetInt32(), Is.EqualTo(1000));
            Assert.That(coverageJson.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(McpSession.Json(pagedToTheCap).GetProperty("uncovered").GetArrayLength(), Is.EqualTo(200));
            Assert.That(McpSession.Json(suiteMap).GetProperty("coverageGaps").GetProperty("units").GetArrayLength(), Is.EqualTo(50));
            Assert.That(McpSession.Json(suiteMap).GetProperty("coverageGaps").GetProperty("truncated").GetBoolean(), Is.True);
        }
    }
}
