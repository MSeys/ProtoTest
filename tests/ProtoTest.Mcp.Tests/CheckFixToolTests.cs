namespace ProtoTest.Mcp.Tests;

using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// The `check_fix` tool through a real MCP session: a proven receipt for the newest run against the one
/// before it, the named reasons when the fix is not proven, and the refusal of one run on both sides.
/// </summary>
[TestFixture]
public sealed class CheckFixToolTests
{
    [Test]
    public async Task CheckFix_ShouldProveTheNewestRunAgainstTheRunBeforeIt()
    {
        using var folder = new TestProjectFolder();
        await WriteAsync(
            Path.Combine(folder.TestResults, "before.prototrace"),
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer."))));
        await WriteAsync(
            Path.Combine(folder.TestResults, "after.prototrace"),
            new RecordedTest("orders are listed", Call("List orders", "GET /orders")));
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var receipt = await session.CallJsonAsync("check_fix");
        var test = receipt.GetProperty("tests")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.GetProperty("proven").GetBoolean(), Is.True);
            Assert.That(test.GetProperty("name").GetString(), Is.EqualTo("orders are listed"));
            Assert.That(test.GetProperty("changedAt").GetProperty("current").GetProperty("subject").GetString(), Is.EqualTo("GET /orders"));
            Assert.That(receipt.GetProperty("brokenTests").GetArrayLength(), Is.EqualTo(0));
            Assert.That(receipt.GetProperty("reportNote").GetString(), Does.Contain("No JSON report"));
        }
    }

    [Test]
    public async Task CheckFix_ShouldNameWhyAFixIsNotProven()
    {
        using var folder = new TestProjectFolder();
        var failing = new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer.")));
        await WriteAsync(Path.Combine(folder.TestResults, "before.prototrace"), failing);
        await WriteAsync(Path.Combine(folder.TestResults, "after.prototrace"), failing);
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var receipt = await session.CallJsonAsync("check_fix", new Dictionary<string, object?> { ["tests"] = new[] { "orders are listed" } });
        var reason = receipt.GetProperty("tests")[0].GetProperty("reasons")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(receipt.GetProperty("proven").GetBoolean(), Is.False);
            Assert.That(reason.GetProperty("code").GetString(), Is.EqualTo("still-failing"));
        }
    }

    [Test]
    public async Task CheckFix_ShouldRefuseOneRunAsBothSides()
    {
        using var folder = new TestProjectFolder();
        var only = folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));
        var runId = TestProjectFolder.RunId(only);

        var result = await session.CallToolAsync("check_fix", new Dictionary<string, object?>
        {
            ["baselineRunId"] = runId,
            ["currentRunIds"] = new[] { runId }
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(McpSession.Text(result), Does.Contain("both the baseline and a current run"));
        }
    }
}
