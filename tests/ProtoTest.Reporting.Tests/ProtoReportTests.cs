namespace ProtoTest.Reporting.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class ProtoReportTests
{
    [Test]
    public void Create_ShouldExcludeNullCoveredAggregatesFromTheSummary()
    {
        var report = ProtoReport.Create(
        [
            // A GraphQL type row is an aggregate: it carries a hit count but no verdict of its own,
            // so it must not be counted as an uncovered unit.
            new ProtoReportItem("Api", "GraphQL type", "Query",
                Kind: ProtoReportItemKinds.Coverage, Status: ProtoReportStatus.Success, Count: 3, IsCovered: null,
                Children:
                [
                    new ProtoReportItem("Api", "GraphQL field", "Query.orders",
                        Kind: ProtoReportItemKinds.Coverage, Status: ProtoReportStatus.Success, Count: 3, IsCovered: true),
                    new ProtoReportItem("Api", "GraphQL field", "Query.other",
                        Kind: ProtoReportItemKinds.Coverage, Status: ProtoReportStatus.Neutral, IsCovered: false)
                ])
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Summary.Total, Is.EqualTo(3), "Aggregates still count as items.");
            Assert.That(report.Summary.CoverageTotal, Is.EqualTo(2));
            Assert.That(report.Summary.Covered, Is.EqualTo(1));
            Assert.That(report.Summary.Uncovered, Is.EqualTo(1));
            Assert.That(report.Summary.CoveragePercentage, Is.EqualTo(50));
        }
    }

    [Test]
    public void Create_ShouldCountHierarchicalCoverageOncePerTopUnit()
    {
        var report = ProtoReport.Create(
        [
            // An OpenAPI-style tree: the endpoint, its response and its properties all record the same
            // calls at different levels, so only the endpoint - the top unit - contributes occurrences.
            new ProtoReportItem("Api", "OpenAPI", "GET /orders",
                Kind: ProtoReportItemKinds.Coverage, Status: ProtoReportStatus.Success, Count: 5, IsCovered: true,
                Children:
                [
                    new ProtoReportItem("Api", "OpenAPI Response", "200",
                        Kind: ProtoReportItemKinds.Coverage, Status: ProtoReportStatus.Success, Count: 5, IsCovered: true,
                        Children:
                        [
                            new ProtoReportItem("Api", "OpenAPI Property", "$.id",
                                Kind: ProtoReportItemKinds.Coverage, Count: 5, IsCovered: true),
                            new ProtoReportItem("Api", "OpenAPI Property", "$.total",
                                Kind: ProtoReportItemKinds.Coverage, IsCovered: false)
                        ])
                ]),
            new ProtoReportItem("Notes", "Observation", "webhook.delivered",
                Kind: ProtoReportItemKinds.Observation, Count: 2)
        ]);

        using (Assert.EnterMultipleScope())
        {
            // 5 endpoint hits + 2 observations; the response and property rows break the 5 down.
            Assert.That(report.Summary.TotalOccurrences, Is.EqualTo(7));
            // Every row with a verdict is still a coverage unit for the coverage totals.
            Assert.That(report.Summary.CoverageTotal, Is.EqualTo(4));
            Assert.That(report.Summary.Covered, Is.EqualTo(3));
        }
    }

    [Test]
    public void Create_ShouldCountUnitsUnderAnAggregateRow()
    {
        var report = ProtoReport.Create(
        [
            // A GraphQL type row is an aggregate: it does not hide the field units below it.
            new ProtoReportItem("Api", "GraphQL type", "Query",
                Kind: ProtoReportItemKinds.Coverage, Count: 3, IsCovered: null,
                Children:
                [
                    new ProtoReportItem("Api", "GraphQL field", "Query.orders",
                        Kind: ProtoReportItemKinds.Coverage, Count: 3, IsCovered: true)
                ])
        ]);

        Assert.That(report.Summary.TotalOccurrences, Is.EqualTo(3));
    }

    [Test]
    public void Create_ShouldCountCapitalizedKindsCaseInsensitively()
    {
        var report = ProtoReport.Create(
        [
            new ProtoReportItem("Api", "REST", "GET /a", Kind: "Coverage", IsCovered: true),
            new ProtoReportItem("Api", "REST", "GET /b", Kind: "Coverage", IsCovered: false),
            new ProtoReportItem("Api", "Notes", "note", Kind: "Finding")
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Summary.CoverageTotal, Is.EqualTo(2));
            Assert.That(report.Summary.Covered, Is.EqualTo(1));
            Assert.That(report.Summary.Uncovered, Is.EqualTo(1));
            Assert.That(report.Summary.Findings, Is.EqualTo(1));
        }
    }

    [Test]
    public void Create_ShouldNotCountTrafficItemsAsCoverage()
    {
        var report = ProtoReport.Create(
        [
            // Observed-but-unasserted fields are a separate kind: they carry a false verdict of their
            // own but never enter the coverage arithmetic, so the assertion-level ratio stays honest.
            new ProtoReportItem("Orders", "REST traffic", "GET /orders · 200",
                Kind: ProtoReportItemKinds.Traffic, IsCovered: null,
                Children:
                [
                    new ProtoReportItem("Orders", "REST traffic", "$.name",
                        Kind: ProtoReportItemKinds.Traffic, IsCovered: false)
                ]),
            new ProtoReportItem("Orders", "OpenAPI", "$.id",
                Kind: ProtoReportItemKinds.Coverage, IsCovered: true)
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Summary.CoverageTotal, Is.EqualTo(1));
            Assert.That(report.Summary.Covered, Is.EqualTo(1));
            Assert.That(report.Summary.Uncovered, Is.Zero);
            Assert.That(report.Summary.CoveragePercentage, Is.EqualTo(100));
        }
    }

    [Test]
    public void CoverageSummaries_ShouldIgnoreNullCoveredAggregatesLikeTheReport()
    {
        var items = new[]
        {
            // Without the exclusion this type aggregate would drag a whole category into a gate's ratio.
            new ProtoReportItem("Api", "GraphQL type", "Query", Kind: ProtoReportItemKinds.Coverage, IsCovered: null),
            new ProtoReportItem("Api", "GraphQL field", "Query.orders", Kind: ProtoReportItemKinds.Coverage, IsCovered: true),
            new ProtoReportItem("Api", "GraphQL field", "Query.other", Kind: ProtoReportItemKinds.Coverage, IsCovered: false)
        };
        var context = new ProtoRunGateContext(items);

        var summaries = context.CoverageSummaries();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].Category, Is.EqualTo("GraphQL field"));
            Assert.That(summaries[0].Covered, Is.EqualTo(1));
            Assert.That(summaries[0].Total, Is.EqualTo(2));
        }
    }

    [Test]
    public void SinkOptions_ShouldDefaultToTheDocumentedPaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new JsonReportSink().OutputPath, Is.EqualTo(Path.Combine(
                "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.json")));
            Assert.That(new HtmlReportSink().OutputPath, Is.EqualTo(Path.Combine(
                "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.html")));
            Assert.That(new JsonReportSinkOptions().Indented, Is.True);
            Assert.That(new HtmlReportSinkOptions().Title, Is.EqualTo("ProtoTest Report"));
        });
    }

    [Test]
    public async Task ReadJson_ShouldRoundTripAReportTheSinkWrote()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoTest.Reporting.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "report.json");
        try
        {
            var items = new[]
            {
                new ProtoReportItem("Api", "OpenAPI", "spec", Kind: ProtoReportItemKinds.Coverage, IsCovered: null,
                    Metadata: new Dictionary<string, object> { ["spec.source"] = "openapi.json", ["spec.hash"] = "abc" }),
                new ProtoReportItem("Api", "OpenAPI", "GET /a", Kind: ProtoReportItemKinds.Coverage,
                    Status: ProtoReportStatus.Success, Count: 2, IsCovered: true)
            };
            var sink = new JsonReportSink(new JsonReportSinkOptions { OutputPath = path });
            await sink.ExportAsync(items);

            var report = ProtoReport.ReadJson(path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(report.Summary.Covered, Is.EqualTo(1));
                Assert.That(report.Summary.CoverageTotal, Is.EqualTo(1));
                Assert.That(report.Items, Has.Count.EqualTo(2));
                Assert.That(report.Items[1].Count, Is.EqualTo(2));
                Assert.That(report.Items[1].Status, Is.EqualTo(ProtoReportStatus.Success));
                Assert.That(report.Items[0].Metadata!["spec.hash"].ToString(), Is.EqualTo("abc"),
                    "Metadata reads back as JSON element text and the spec keys stay readable.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public void ReadJson_ShouldNameAMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-missing-{Guid.NewGuid():N}.json");

        var exception = Assert.Throws<InvalidOperationException>(() => ProtoReport.ReadJson(path));

        Assert.That(exception!.Message, Does.Contain(path));
    }

    [Test]
    public void ReadJson_ShouldNameContentThatIsNotAReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-invalid-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "not": "a report" }""");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => ProtoReport.ReadJson(path));

            Assert.That(exception!.Message, Does.Contain("not a ProtoTest report"));
        }
        finally { File.Delete(path); }
    }
}
