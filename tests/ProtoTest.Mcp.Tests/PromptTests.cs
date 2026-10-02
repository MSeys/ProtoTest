namespace ProtoTest.Mcp.Tests;

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// The job prompts and the coverage suggestions through a real MCP session: the three prompts are
/// listed, each ends with the tool that judges the job, and an uncovered unit names the test to start from.
/// </summary>
[TestFixture]
public sealed class PromptTests
{
    [Test]
    public async Task Prompts_ShouldOfferTheThreeJobs()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var prompts = await session.Client.ListPromptsAsync();

        Assert.That(prompts.Select(prompt => prompt.Name), Is.EquivalentTo(new[] { "fix_failure", "cover_change", "improve_tests" }));
    }

    [Test]
    public async Task FixFailure_ShouldNameTheTestAndEndWithCheckFix()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var prompt = await session.Client.GetPromptAsync("fix_failure", new Dictionary<string, object?> { ["test"] = "orders match their shape" });
        var text = Text(prompt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("'orders match their shape'"));
            Assert.That(text, Does.Contain("get_suite_map"));
            Assert.That(text, Does.Contain("compare_runs"));
            Assert.That(text, Does.Contain("done only when check_fix says proven"));
        }
    }

    [Test]
    public async Task CoverChangeAndImproveTests_ShouldEndWithTheirJudges()
    {
        using var folder = new TestProjectFolder();
        folder.AddFixture("run-failed");
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var cover = Text(await session.Client.GetPromptAsync("cover_change", new Dictionary<string, object?>
        {
            ["change"] = "the new DELETE /orders/{id} endpoint",
            ["target"] = "Shop:Api"
        }));
        var improve = Text(await session.Client.GetPromptAsync("improve_tests"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cover, Does.Contain("the new DELETE /orders/{id} endpoint"));
            Assert.That(cover, Does.Contain("get_coverage with target 'Shop:Api'"));
            Assert.That(cover, Does.Contain("review_tests"));
            Assert.That(cover, Does.Contain("it must fail"));
            Assert.That(improve, Does.Contain("review_tests"));
            Assert.That(improve, Does.Contain("no test may read broken"));
        }
    }

    [Test]
    public async Task GetCoverage_ShouldSuggestTheTestToStartFrom()
    {
        using var folder = new TestProjectFolder();
        var trace = Path.Combine(folder.TestResults, "run.prototrace");
        await WriteAsync(
            trace,
            builder =>
            {
                builder.AddSink(new JsonReportSink { OutputPath = Path.Combine(folder.Path, "report.json") });
                builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new Source(
                [
                    new("Shop:Api", "OpenAPI", "GET /orders/{id}", ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, 1, true,
                        Children: [new("Shop:Api", "OpenAPI Response", "404", ProtoReportItemKinds.Coverage, IsCovered: false)])
                ])));
            },
            new RecordedTest("an order is read", Call("Read order", "GET /orders/42"), Check("order shape")));
        await using var session = await McpSession.StartAsync(ProtoTestMcpOptions.ForProject(folder.Path));

        var coverage = await session.CallJsonAsync("get_coverage");
        var suggestion = coverage.GetProperty("suggestions")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.GetProperty("uncovered")[0].GetProperty("endpoint").GetString(), Is.EqualTo("GET /orders/{id}"));
            Assert.That(suggestion.GetProperty("action").GetString(), Is.EqualTo("extend"));
            Assert.That(suggestion.GetProperty("endpoint").GetString(), Is.EqualTo("GET /orders/{id}"));
            Assert.That(suggestion.GetProperty("test").GetString(), Is.EqualTo("an order is read"));
        }
    }

    private static string Text(GetPromptResult result)
        => string.Join("\n", result.Messages.Select(message => message.Content).OfType<TextContentBlock>().Select(block => block.Text));

    private sealed class Source(IEnumerable<ProtoReportItem> items) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems() => items;
    }
}
