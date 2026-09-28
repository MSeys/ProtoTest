namespace ProtoTest.Mcp.Tests;

using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// The `get_diagnosis` tool through a real MCP session: the digest of the newest run, one failing
/// test's context package, the honest absence of a report, and the named errors for a green test or an
/// unknown detail.
/// </summary>
[TestFixture]
public sealed class DiagnosisToolTests
{
    [Test]
    public async Task GetDiagnosis_ShouldReturnTheDigestOfTheNewestRun()
    {
        using var folder = new TestProjectFolder();
        var failed = folder.AddFixture("run-failed");
        folder.AddFixture("run-passed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var diagnosis = await session.CallJsonAsync("get_diagnosis");
        var failure = diagnosis.GetProperty("failures")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnosis.GetProperty("runId").GetString(), Is.EqualTo(TestProjectFolder.RunId(failed)));
            Assert.That(diagnosis.GetProperty("digestVersion").GetString(), Is.EqualTo("1"));
            Assert.That(diagnosis.GetProperty("outcomes").GetProperty("failed").GetInt32(), Is.EqualTo(1));
            Assert.That(diagnosis.GetProperty("coverage").GetProperty("covered").GetInt32(), Is.EqualTo(2));
            Assert.That(failure.GetProperty("rule").GetString(), Is.EqualTo("assertion"));
            Assert.That(failure.GetProperty("failure").GetProperty("kind").GetString(), Is.EqualTo("assert.json.shape"));
            Assert.That(failure.GetProperty("mismatches")[0].GetProperty("path").GetString(), Is.EqualTo("$.orderId"));
            Assert.That(failure.GetProperty("artifacts").GetArrayLength(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetDiagnosis_ShouldReturnTheContextPackage()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var context = await session.CallJsonAsync("get_diagnosis", new Dictionary<string, object?> { ["detail"] = "context" });
        var failure = context.GetProperty("failure");
        var report = context.GetProperty("report");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.GetProperty("rule").GetString(), Is.EqualTo("assertion"));
            Assert.That(failure.GetProperty("kind").GetString(), Is.EqualTo("assert.json.shape"));
            Assert.That(failure.GetProperty("attributes").GetProperty("shape.mismatch_count").GetString(), Is.EqualTo("1"));
            Assert.That(context.GetProperty("ancestors").GetArrayLength(), Is.EqualTo(1));
            Assert.That(context.GetProperty("ancestors")[0].GetProperty("kind").GetString(), Is.EqualTo("test.execution"));
            Assert.That(context.GetProperty("sections")[0].GetProperty("kind").GetString(), Is.EqualTo("checks"));
            // The committed fixture embedded no source files, so the context says so instead of guessing.
            Assert.That(context.GetProperty("source").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(context.GetProperty("sourceAbsentReason").GetString(), Does.Contain("does not embed"));
            Assert.That(report.GetProperty("coverage").GetProperty("total").GetInt32(), Is.EqualTo(4));
            Assert.That(report.GetProperty("coverageRows").GetArrayLength(), Is.EqualTo(0));
            Assert.That(report.GetProperty("absentReason").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [Test]
    public async Task GetDiagnosis_ShouldStateAnAbsentReportAndRefuseAGreenTest()
    {
        using var folder = new TestProjectFolder();
        var passed = folder.AddFixture("run-passed");
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var green = await session.CallJsonAsync("get_diagnosis", new Dictionary<string, object?> { ["runId"] = TestProjectFolder.RunId(passed) });
        var context = await session.CallToolAsync("get_diagnosis", new Dictionary<string, object?>
        {
            ["runId"] = TestProjectFolder.RunId(passed),
            ["detail"] = "context"
        });
        var unknownDetail = await session.CallToolAsync("get_diagnosis", new Dictionary<string, object?> { ["detail"] = "everything" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(green.GetProperty("failures").GetArrayLength(), Is.EqualTo(0));
            Assert.That(green.GetProperty("coverage").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(green.GetProperty("coverageAbsentReason").GetString(), Does.Contain("No JSON report artifact"));
            Assert.That(context.IsError, Is.True);
            Assert.That(McpSession.Text(context), Does.Contain("succeeded"));
            Assert.That(unknownDetail.IsError, Is.True);
            Assert.That(McpSession.Text(unknownDetail), Does.Contain("Unknown detail"));
        }
    }
}
