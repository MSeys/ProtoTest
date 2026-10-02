namespace ProtoTest.Diagnosis.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.TestSupport;
using ProtoTest.Traces;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>
/// <see cref="ProtoDiagnosis.SuggestCoverage"/> over a real run with an embedded report: extend the test
/// that already calls the endpoint, or start a new test from the closest recorded call.
/// </summary>
[TestFixture]
public sealed class CoverageSuggestionTests
{
    [Test]
    public async Task SuggestCoverage_ShouldExtendTheTestThatAlreadyCallsTheEndpoint()
    {
        using var trace = new TemporaryTrace("suggest-extend");
        await WriteAsync(
            trace.Path,
            WithReport(trace.Path,
                new ProtoReportItem("Shop:Api", "OpenAPI", "GET /orders/{id}", ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, 1, true,
                    Children: [new("Shop:Api", "OpenAPI Response", "404", ProtoReportItemKinds.Coverage, IsCovered: false)])),
            new RecordedTest("an order is read", Call("Read order", "GET /orders/42"), Check("order shape")));

        var suggestion = ProtoDiagnosis.SuggestCoverage(ProtoTraceArchive.Open(trace.Path)).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suggestion.Identifier, Is.EqualTo("404"));
            Assert.That(suggestion.Endpoint, Is.EqualTo("GET /orders/{id}"), "a nested unit belongs to its endpoint");
            Assert.That(suggestion.Action, Is.EqualTo(ProtoCoverageActions.Extend));
            Assert.That(suggestion.Test, Is.EqualTo("an order is read"));
            Assert.That(suggestion.Reason, Does.Contain("already calls GET /orders/{id}"));
        }
    }

    [Test]
    public async Task SuggestCoverage_ShouldStartANewTestFromTheSamePathOrResource()
    {
        using var trace = new TemporaryTrace("suggest-new");
        await WriteAsync(
            trace.Path,
            WithReport(trace.Path,
                new ProtoReportItem("Shop:Api", "OpenAPI", "DELETE /orders/{id}", ProtoReportItemKinds.Coverage, IsCovered: false),
                new ProtoReportItem("Shop:Api", "OpenAPI", "POST /orders/{id}/cancel", ProtoReportItemKinds.Coverage, IsCovered: false),
                new ProtoReportItem("Shop:Api", "OpenAPI", "GET /tariffs", ProtoReportItemKinds.Coverage, IsCovered: false)),
            new RecordedTest("an order is read", Call("Read order", "GET /orders/42"), Check("order shape")));

        var suggestions = ProtoDiagnosis.SuggestCoverage(ProtoTraceArchive.Open(trace.Path)).ToDictionary(s => s.Identifier);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suggestions.Values.Select(s => s.Action), Is.All.EqualTo(ProtoCoverageActions.New));
            Assert.That(suggestions["DELETE /orders/{id}"].Reason, Does.Contain("on the same path"));
            Assert.That(suggestions["POST /orders/{id}/cancel"].Reason, Does.Contain("on the same resource"));
            Assert.That(suggestions["GET /tariffs"].Reason, Does.Contain("a recorded call of the same kind"));
            Assert.That(suggestions.Values.Select(s => s.Test), Is.All.EqualTo("an order is read"));
        }
    }

    [Test]
    public async Task SuggestCoverage_ShouldIgnoreAFailedTestAsAStartingPoint()
    {
        using var trace = new TemporaryTrace("suggest-failed");
        await WriteAsync(
            trace.Path,
            WithReport(trace.Path, new ProtoReportItem("Shop:Api", "OpenAPI", "GET /tariffs", ProtoReportItemKinds.Coverage, IsCovered: false)),
            new RecordedTest("an order is read", FailedCall("Read order", "GET /orders/42", new TimeoutException("No answer."))));

        var suggestion = ProtoDiagnosis.SuggestCoverage(ProtoTraceArchive.Open(trace.Path)).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suggestion.Test, Is.Null, "a failing test is not a model to copy");
            Assert.That(suggestion.Reason, Does.Contain("no call to start from"));
        }
    }

    [Test]
    public async Task SuggestCoverage_ShouldExtendTheBrowserTestThatOpensAnUnverifiedPage()
    {
        using var trace = new TemporaryTrace("suggest-page");
        await WriteAsync(
            trace.Path,
            WithReport(trace.Path,
                new ProtoReportItem("Web", "Web", "/login", ProtoReportItemKinds.Coverage, IsCovered: false),
                new ProtoReportItem("Web", "Web", "/settings", ProtoReportItemKinds.Coverage, IsCovered: false)),
            new RecordedTest("a project appears on the page", Navigate("http://127.0.0.1:60278/login"), Check("status")));

        var suggestions = ProtoDiagnosis.SuggestCoverage(ProtoTraceArchive.Open(trace.Path)).ToDictionary(s => s.Identifier);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suggestions["/login"].Action, Is.EqualTo(ProtoCoverageActions.Extend));
            Assert.That(suggestions["/login"].Reason, Does.Contain("opens /login but checks nothing on it"));
            Assert.That(suggestions["/settings"].Action, Is.EqualTo(ProtoCoverageActions.New));
            Assert.That(suggestions["/settings"].Test, Is.EqualTo("a project appears on the page"), "a recorded browser journey is the model");
        }
    }

    [Test]
    public async Task SuggestCoverage_ShouldReturnNothingWithoutAnEmbeddedReport()
    {
        using var trace = new TemporaryTrace("suggest-no-report");
        await WriteAsync(trace.Path, new RecordedTest("an order is read", Call("Read order", "GET /orders/42")));

        Assert.That(ProtoDiagnosis.SuggestCoverage(ProtoTraceArchive.Open(trace.Path)), Is.Empty);
    }

    private static Action<ProtoHostBuilder> WithReport(string tracePath, params ProtoReportItem[] items)
        => builder =>
        {
            builder.AddSink(new JsonReportSink { OutputPath = $"{Path.ChangeExtension(tracePath, null)}.report.json" });
            builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new Source(items)));
        };

    private sealed class Source(IEnumerable<ProtoReportItem> items) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems() => items;
    }
}
