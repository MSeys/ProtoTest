namespace ProtoTest.Mcp.Tests;

using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// The read-only tools over real, committed trace fixtures, driven through a real MCP session: run
/// discovery, the failure entry with its mismatches and artifacts, the embedded coverage report, and
/// the honest errors a missing file or folder produces.
/// </summary>
[TestFixture]
public sealed class RunToolsTests
{
    [Test]
    public async Task ListRuns_ShouldReturnNewestFirstWithOutcomesAndASkippedArchive()
    {
        using var folder = new TestProjectFolder();
        var failed = folder.AddFixture("run-failed");
        var passed = folder.AddFixture("run-passed");
        File.WriteAllText(Path.Combine(folder.TestResults, "broken.prototrace"), "not a trace");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var runs = (await session.CallJsonAsync("list_runs")).GetProperty("runs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(runs.GetArrayLength(), Is.EqualTo(2));
            Assert.That(runs[0].GetProperty("runId").GetString(), Is.EqualTo(TestProjectFolder.RunId(failed)));
            Assert.That(runs[1].GetProperty("runId").GetString(), Is.EqualTo(TestProjectFolder.RunId(passed)));
            Assert.That(runs[0].GetProperty("outcomes").GetProperty("failed").GetInt32(), Is.EqualTo(1));
            Assert.That(runs[0].GetProperty("outcomes").GetProperty("succeeded").GetInt32(), Is.EqualTo(1));
            Assert.That(
                runs[0].GetProperty("failingTests")[0].GetProperty("name").GetString(),
                Is.EqualTo("orders match their shape"));
            Assert.That(runs[0].GetProperty("startedAtUtc").GetString(), Is.Not.Empty);
            Assert.That(runs[0].GetProperty("traceFile").GetString(), Does.EndWith("run-failed.prototrace"));
        }
    }

    [Test]
    public async Task ListRuns_ShouldUseTestResultsFirstAndFallBackToThePrunedTree()
    {
        using var withDefaultLocation = new TestProjectFolder();
        withDefaultLocation.AddFixture("run-passed");
        var customFolder = Path.Combine(withDefaultLocation.Path, "custom");
        Directory.CreateDirectory(customFolder);
        File.Copy(TestProjectFolder.FixturePath("run-failed"), Path.Combine(customFolder, "run-failed.prototrace"));
        await using (var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(withDefaultLocation.Path)))
        {
            var runs = (await session.CallJsonAsync("list_runs")).GetProperty("runs");

            // TestResults holds a run, so the tree is not walked.
            Assert.That(runs.GetArrayLength(), Is.EqualTo(1));
            Assert.That(runs[0].GetProperty("traceFile").GetString(), Does.EndWith("run-passed.prototrace"));
        }

        using var withoutDefaultLocation = new TestProjectFolder();
        var customOnly = Path.Combine(withoutDefaultLocation.Path, "custom");
        Directory.CreateDirectory(Path.Combine(customOnly, "bin"));
        File.Copy(TestProjectFolder.FixturePath("run-failed"), Path.Combine(customOnly, "run-failed.prototrace"));
        File.Copy(
            TestProjectFolder.FixturePath("run-passed"),
            Path.Combine(customOnly, "bin", "pruned.prototrace"));
        await using var fallback = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(withoutDefaultLocation.Path));
        var found = (await fallback.CallJsonAsync("list_runs")).GetProperty("runs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found.GetArrayLength(), Is.EqualTo(1));
            Assert.That(found[0].GetProperty("traceFile").GetString(), Does.EndWith("run-failed.prototrace"));
        }
    }

    [Test]
    public async Task ListRuns_ShouldClampTheLimitAndReportTruncation()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        folder.AddFixture("run-passed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var one = await session.CallJsonAsync("list_runs", new Dictionary<string, object?> { ["limit"] = 1 });
        var zero = await session.CallJsonAsync("list_runs", new Dictionary<string, object?> { ["limit"] = 0 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(one.GetProperty("runs").GetArrayLength(), Is.EqualTo(1));
            Assert.That(one.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(zero.GetProperty("runs").GetArrayLength(), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetFailure_ShouldReadTheNewestRunsFailureWithMismatchesAndArtifacts()
    {
        using var folder = new TestProjectFolder();
        var failed = folder.AddFixture("run-failed");
        folder.AddFixture("run-passed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var failure = await session.CallJsonAsync("get_failure");

        var test = failure.GetProperty("test");
        var operation = failure.GetProperty("failure");
        var failedOperation = failure.GetProperty("failedOperations")[0];
        var mismatch = failure.GetProperty("mismatches")[0];
        var artifacts = failure.GetProperty("artifacts");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.GetProperty("runId").GetString(), Is.EqualTo(TestProjectFolder.RunId(failed)));
            Assert.That(test.GetProperty("name").GetString(), Is.EqualTo("orders match their shape"));
            Assert.That(test.GetProperty("outcome").GetString(), Is.EqualTo("failed"));
            // The failure entry and the viewer agree: the deepest failing operation, here the shape check.
            Assert.That(operation.GetProperty("kind").GetString(), Is.EqualTo("assert.json.shape"));
            Assert.That(operation.GetProperty("status").GetString(), Is.EqualTo("failed"));
            Assert.That(operation.GetProperty("errorMessage").GetString(), Is.Not.Empty);
            Assert.That(failedOperation.GetProperty("kind").GetString(), Is.EqualTo("assert.json.shape"));
            Assert.That(failedOperation.GetProperty("sourceFile").GetString(), Does.EndWith("Program.cs"));
            Assert.That(failedOperation.GetProperty("sourceLine").GetInt32(), Is.GreaterThan(0));
            Assert.That(mismatch.GetProperty("propertyPath").GetString(), Is.EqualTo("$.orderId"));
            Assert.That(mismatch.GetProperty("expected").GetInt32(), Is.EqualTo(7));
            Assert.That(mismatch.GetProperty("actual").GetInt32(), Is.EqualTo(42));
            Assert.That(failure.GetProperty("mismatchesTruncated").GetBoolean(), Is.False);
            Assert.That(artifacts.GetArrayLength(), Is.EqualTo(2));
            Assert.That(
                artifacts.EnumerateArray().Select(artifact => artifact.GetProperty("name").GetString()),
                Has.Some.EndsWith("rest-01-expected-shape"));
        }
    }

    [Test]
    public async Task GetFailure_ShouldStateASucceededOrANamedTestHonestly()
    {
        using var folder = new TestProjectFolder();
        var passed = folder.AddFixture("run-passed");
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var namedPassingTest = await session.CallJsonAsync("get_failure", new Dictionary<string, object?> { ["testId"] = "00001" });
        var allGreenRun = await session.CallJsonAsync("get_failure", new Dictionary<string, object?> { ["runId"] = TestProjectFolder.RunId(passed) });
        var unknownTest = await session.CallToolAsync("get_failure", new Dictionary<string, object?> { ["testId"] = "does-not-exist" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(namedPassingTest.GetProperty("failure").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(namedPassingTest.GetProperty("note").GetString(), Does.Contain("succeeded"));
            Assert.That(allGreenRun.GetProperty("note").GetString(), Does.Contain("succeeded"));
            Assert.That(allGreenRun.GetProperty("failure").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(unknownTest.IsError, Is.True);
            Assert.That(McpSession.Text(unknownTest), Does.Contain("does-not-exist"));
        }
    }

    [Test]
    public async Task GetCoverage_ShouldReadTheEmbeddedReportWithFiltersAndPaging()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-passed");
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var totals = (await session.CallJsonAsync("get_coverage")).GetProperty("totals");
        var all = await session.CallJsonAsync("get_coverage");
        var filtered = await session.CallJsonAsync("get_coverage", new Dictionary<string, object?>
        {
            ["target"] = "Northstar:Api",
            ["category"] = "OpenAPI Property"
        });
        var paged = await session.CallJsonAsync("get_coverage", new Dictionary<string, object?> { ["limit"] = 1 });
        var totalsOnly = await session.CallJsonAsync("get_coverage", new Dictionary<string, object?> { ["includeUncovered"] = false });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(totals.GetProperty("coverageTotal").GetInt32(), Is.EqualTo(4));
            Assert.That(totals.GetProperty("covered").GetInt32(), Is.EqualTo(2));
            Assert.That(totals.GetProperty("uncovered").GetInt32(), Is.EqualTo(2));
            Assert.That(totals.GetProperty("coveragePercentage").GetDouble(), Is.EqualTo(50));
            Assert.That(all.GetProperty("uncoveredTotal").GetInt32(), Is.EqualTo(2));
            Assert.That(all.GetProperty("reportArtifact").GetProperty("name").GetString(), Does.EndWith("report.json"));
            Assert.That(
                all.GetProperty("uncovered").EnumerateArray().Select(unit => unit.GetProperty("identifier").GetString()),
                Is.EquivalentTo(new[] { "$.total", "POST /api/v1/orders" }));
            Assert.That(filtered.GetProperty("uncoveredTotal").GetInt32(), Is.EqualTo(1));
            Assert.That(filtered.GetProperty("uncovered")[0].GetProperty("identifier").GetString(), Is.EqualTo("$.total"));
            Assert.That(paged.GetProperty("uncovered").GetArrayLength(), Is.EqualTo(1));
            Assert.That(paged.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(totalsOnly.GetProperty("uncovered").GetArrayLength(), Is.EqualTo(0));
            Assert.That(totalsOnly.GetProperty("totals").GetProperty("coverageTotal").GetInt32(), Is.EqualTo(4));
        }
    }

    [Test]
    public async Task GetCoverage_ShouldStateAMissingReportInsteadOfInventingNumbers()
    {
        using var folder = new TestProjectFolder();
        var passed = folder.AddFixture("run-passed");
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var coverage = await session.CallJsonAsync("get_coverage", new Dictionary<string, object?> { ["runId"] = TestProjectFolder.RunId(passed) });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.GetProperty("note").GetString(), Does.Contain("No JSON report artifact"));
            Assert.That(coverage.GetProperty("totals").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(coverage.GetProperty("uncovered").GetArrayLength(), Is.EqualTo(0));
        }
    }

    [Test]
    public async Task Tools_ShouldNameMissingFilesAndFoldersInsteadOfGuessing()
    {
        var missingTrace = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.prototrace");
        await using var traceSession = await McpSession.StartAsync(ProtoTestMcpOptions.ForTrace(missingTrace));

        var missingFile = await traceSession.CallToolAsync("list_runs");
        var missingCoverage = await traceSession.CallToolAsync("get_coverage");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingFile.IsError, Is.True);
            Assert.That(McpSession.Text(missingFile), Does.Contain(missingTrace));
            Assert.That(missingCoverage.IsError, Is.True);
            Assert.That(McpSession.Text(missingCoverage), Does.Contain(missingTrace));
        }

        var missingFolder = Path.Combine(Path.GetTempPath(), $"missing-folder-{Guid.NewGuid():N}");
        await using var folderSession = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(missingFolder));
        var folderResult = await folderSession.CallToolAsync("list_runs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(folderResult.IsError, Is.True);
            Assert.That(McpSession.Text(folderResult), Does.Contain("Project folder not found"));
        }
    }

    [Test]
    public async Task ListRuns_ShouldRefuseAFolderOnATraceBoundServer()
    {
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForTrace(TestProjectFolder.FixturePath("run-failed")));

        var result = await session.CallToolAsync("list_runs", new Dictionary<string, object?>
        {
            ["folder"] = Path.GetTempPath()
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(McpSession.Text(result), Does.Contain("not accepted"));
        }
    }
}
