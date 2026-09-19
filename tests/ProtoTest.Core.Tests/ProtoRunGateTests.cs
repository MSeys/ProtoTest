namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public class ProtoRunGateTests
{
    [Test]
    public async Task FailingGate_ShouldFailTheRun()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddRunGate("coverage is complete", _ =>
            ProtoRunGateResult.Failed("Only 1 of 2 endpoints were called."));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        var exception = Assert.ThrowsAsync<ProtoRunGateException>(() => host.StopAsync());

        // Assert
        Assert.That(exception!.Failures.Single().Name, Is.EqualTo("coverage is complete"));
    }

    [Test]
    public async Task AdapterLifetime_ShouldSurfaceGateFailuresFromAssemblyTeardown()
    {
        // Arrange: ProtoTestHostLifetime.StopAsync is what every adapter calls in its assembly teardown.
        var lifetime = new ProtoTestHostLifetime();
        await lifetime.StartAsync(builder => builder.AddRunGate(
            "no regressions",
            _ => ProtoRunGateResult.Failed("A regression was detected.")));

        // Act
        var exception = Assert.ThrowsAsync<ProtoRunGateException>(() => lifetime.StopAsync());

        // Assert
        Assert.That(exception!.Failures.Single().Name, Is.EqualTo("no regressions"));
    }

    [Test]
    public async Task GateEvaluations_ShouldBeRecordedInTheRunTrace()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddRunGate("coverage is complete", _ => ProtoRunGateResult.Warning("Worth a look."));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: a gate judges the run, so it belongs to the run's trace.
        var entry = host.Trace.Snapshot().Entries!.Single(item => item.Kind == "gate.evaluate");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Phase, Is.EqualTo(ProtoTracePhase.Run));
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(entry.Attributes["gate.name"], Is.EqualTo("coverage is complete"));
            Assert.That(entry.Attributes["gate.message"], Is.EqualTo("Worth a look."));
        }
    }

    [Test]
    public async Task GateEvaluations_ShouldReachTheSpanWireAsScopeEvents()
    {
        // Arrange
        var tracePath = Path.Combine(Path.GetTempPath(), "ProtoTest.Core.Tests", Guid.NewGuid().ToString("N"), "run.prototrace");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = tracePath);
        builder.AddRunGate("coverage is complete", _ => ProtoRunGateResult.Warning("Worth a look."));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: a verdict has no operation above it, and must still reach spans.json - on the run's scope.
        using var archive = System.IO.Compression.ZipFile.OpenRead(tracePath);
        using var reader = new StreamReader(archive.GetEntry("spans.json")!.Open());
        using var spans = System.Text.Json.JsonDocument.Parse(await reader.ReadToEndAsync());
        var runScope = spans.RootElement.GetProperty("resourceSpans").EnumerateArray()
            .Single(group => group.GetProperty("resource").GetProperty("attributes").TryGetProperty("runId", out _))
            .GetProperty("scopeSpans")[0];
        var gate = runScope.GetProperty("events").EnumerateArray()
            .Single(item => item.TryGetProperty("kind", out var kind) && kind.GetString() == "gate.evaluate");
        Assert.That(gate.GetProperty("outcome").GetString(), Is.EqualTo("partial"));
    }

    [Test]
    public async Task AdvisoryAndSkippedGates_ShouldNotFailTheRun()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddRunGate("advisory", _ => ProtoRunGateResult.Warning("Worth a look."));
        builder.AddRunGate("not applicable", _ => ProtoRunGateResult.Skipped());
        await using var host = builder.Build();

        // Act & Assert
        await host.StartAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task GateVerdicts_ShouldReachTheReportAsTheirOwnKind()
    {
        // Arrange
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        builder.AddRunGate("coverage is complete", _ =>
            ProtoRunGateResult.Failed("Only half covered.", ["GET /orders", "GET /invoices"]));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        try
        {
            await host.StopAsync();
        }
        catch (ProtoRunGateException)
        {
            // The report is written before the run fails.
        }

        // Assert: a gate verdict is not a finding; the report shows it in its own category.
        var verdict = sink.Items.Single(item => item.Category == "Gate");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(verdict.Identifier, Is.EqualTo("coverage is complete"));
            Assert.That(verdict.Kind, Is.EqualTo(ProtoReportItemKinds.Gate));
            Assert.That(verdict.Status, Is.EqualTo(ProtoReportStatus.Error));
            Assert.That(verdict.Message, Is.EqualTo("Only half covered."));
            Assert.That(verdict.Tags, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Gate_ShouldSeeWhatTheCollectorsProduced()
    {
        // Arrange
        var collector = new TestCoverageCollector("api");
        collector.Collect(new ProtoObservation("api", "endpoint", "GET /orders"));
        ProtoCoverageSummary? seen = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(collector));
        builder.AddRunGate("api coverage", context =>
        {
            seen = context.CoverageFor("api");
            return ProtoRunGateResult.Passed();
        });
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert
        Assert.That(seen, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen!.Covered, Is.EqualTo(1));
            Assert.That(seen.Total, Is.EqualTo(1));
            Assert.That(seen.Ratio, Is.EqualTo(1d));
        }
    }

    [Test]
    public async Task GateThatThrows_ShouldBeReportedAsAFailure()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddRunGate("broken", _ => throw new InvalidOperationException("gate exploded"));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        var exception = Assert.ThrowsAsync<ProtoRunGateException>(() => host.StopAsync());

        // Assert
        Assert.That(exception!.Failures.Single().Message, Does.Contain("gate exploded"));
    }

    [Test]
    public void Context_ShouldAggregateCoveragePerTargetAndCategory()
    {
        // Arrange
        var items = new[]
        {
            new ProtoReportItem("api", "Endpoints", "GET /a", Kind: ProtoReportItemKinds.Coverage, IsCovered: true),
            new ProtoReportItem("api", "Endpoints", "GET /b", Kind: ProtoReportItemKinds.Coverage, IsCovered: false),
            new ProtoReportItem("api", "Endpoints", "POST /c", Kind: ProtoReportItemKinds.Coverage, IsCovered: true),
            new ProtoReportItem("api", "Findings", "note", Kind: ProtoReportItemKinds.Finding)
        };

        // Act
        var context = new ProtoRunGateContext(items);
        var coverage = context.CoverageFor("api");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.CoverageSummaries(), Has.Count.EqualTo(1));
            Assert.That(coverage!.Covered, Is.EqualTo(2));
            Assert.That(coverage.Total, Is.EqualTo(3));
            Assert.That(coverage.Ratio, Is.EqualTo(2d / 3d));
            Assert.That(context.ItemsOfKind(ProtoReportItemKinds.Finding), Has.Exactly(1).Items);
        }
    }

    [Test]
    public void Context_ShouldSeeCoverageUnitsNestedUnderCoverageRoots()
    {
        // Arrange: GraphQL reports a type as an aggregate root (no verdict) with covered fields beneath it.
        var items = new[]
        {
            new ProtoReportItem(
                "api",
                "GraphQL type",
                "Query",
                Kind: ProtoReportItemKinds.Coverage,
                IsCovered: null,
                Children:
                [
                    new ProtoReportItem(
                        "api",
                        "GraphQL field",
                        "Query.orders",
                        Kind: ProtoReportItemKinds.Coverage,
                        IsCovered: true),
                    new ProtoReportItem(
                        "api",
                        "GraphQL field",
                        "Query.customers",
                        Kind: ProtoReportItemKinds.Coverage,
                        IsCovered: false)
                ])
        };

        // Act
        var context = new ProtoRunGateContext(items);
        var coverage = context.CoverageFor("api");

        // Assert: the gate summary counts child units, exactly as the report summary flattens first.
        Assert.That(coverage, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.CoverageSummaries(), Has.Count.EqualTo(1));
            Assert.That(coverage!.Covered, Is.EqualTo(1));
            Assert.That(coverage.Total, Is.EqualTo(2));
        }
    }

    [Test]
    public void Context_ShouldAggregateAllCategoriesWhenAskingForOneTarget()
    {
        // Arrange: one target reports coverage under two categories.
        var items = new[]
        {
            new ProtoReportItem("api", "Endpoints", "GET /a", Kind: ProtoReportItemKinds.Coverage, IsCovered: true),
            new ProtoReportItem("api", "Endpoints", "GET /b", Kind: ProtoReportItemKinds.Coverage, IsCovered: false),
            new ProtoReportItem("api", "Fields", "Order.id", Kind: ProtoReportItemKinds.Coverage, IsCovered: true)
        };

        // Act
        var context = new ProtoRunGateContext(items);
        var aggregate = context.CoverageFor("api");
        var endpoints = context.CoverageFor("api", "Endpoints");

        // Assert: a gate asking for the target sees every category, not whichever came first.
        Assert.Multiple(() =>
        {
            Assert.That(context.CoverageSummaries(), Has.Count.EqualTo(2));
            Assert.That(aggregate, Is.Not.Null);
            Assert.That(aggregate!.Covered, Is.EqualTo(2));
            Assert.That(aggregate.Total, Is.EqualTo(3));
            Assert.That(aggregate.Percentage, Is.EqualTo(new ProtoCoverageTotals(3, 2).Percentage));
            Assert.That(endpoints, Is.Not.Null);
            Assert.That(endpoints!.Covered, Is.EqualTo(1));
            Assert.That(endpoints.Total, Is.EqualTo(2));
        });
    }

    [Test]
    public void Context_ShouldExposeAnyCollectedEvidenceNotJustCoverage()
    {
        // Arrange: a collector reporting findings and metrics, with no coverage at all.
        var items = new[]
        {
            new ProtoReportItem("billing", "Findings", "orphaned invoices", Kind: ProtoReportItemKinds.Finding, Status: ProtoReportStatus.Error),
            new ProtoReportItem("billing", "Metrics", "avg latency", Kind: ProtoReportItemKinds.Metric, Status: ProtoReportStatus.Warning, Value: 812d, Unit: "ms")
        };
        var context = new ProtoRunGateContext(items);

        // Act
        var errors = context.WithStatus(ProtoReportStatus.Error).ToArray();
        var latency = context.InCategory("Metrics").Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors, Has.Exactly(1).Items);
            Assert.That(errors[0].Identifier, Is.EqualTo("orphaned invoices"));
            Assert.That(latency.Value, Is.EqualTo(812d));
            Assert.That(context.ForTarget("billing"), Has.Exactly(2).Items);
            Assert.That(context.CoverageSummaries(), Is.Empty);
        }
    }

    [Test]
    public void Context_ShouldGroupCoverageIgnoringTargetAndCategoryCase()
    {
        var items = new[]
        {
            new ProtoReportItem("API", "Endpoints", "GET /a", Kind: ProtoReportItemKinds.Coverage, IsCovered: true),
            new ProtoReportItem("api", "endpoints", "GET /b", Kind: ProtoReportItemKinds.Coverage, IsCovered: false)
        };

        var context = new ProtoRunGateContext(items);

        // The lookups ignore case, so the grouping must too: one row, both units.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.CoverageSummaries(), Has.Count.EqualTo(1));
            var coverage = context.CoverageFor("Api");
            Assert.That(coverage, Is.Not.Null);
            Assert.That(coverage!.Covered, Is.EqualTo(1));
            Assert.That(coverage.Total, Is.EqualTo(2));
        }
    }

    [Test]
    public void Context_ShouldFlattenNestedItemsForTheQueryHelpers()
    {
        // A GraphQL-style tree: the type is an aggregate root, the fields beneath it carry the verdicts.
        var items = new[]
        {
            new ProtoReportItem(
                "api",
                "GraphQL type",
                "Query",
                Kind: ProtoReportItemKinds.Coverage,
                IsCovered: null,
                Children:
                [
                    new ProtoReportItem("api", "GraphQL field", "Query.orders", Kind: ProtoReportItemKinds.Coverage, IsCovered: true),
                    new ProtoReportItem(
                        "api",
                        "GraphQL field",
                        "Query.customers",
                        Kind: ProtoReportItemKinds.Coverage,
                        Status: ProtoReportStatus.Warning,
                        IsCovered: false)
                ])
        };

        var context = new ProtoRunGateContext(items);

        // Coverage summaries flatten, so every query helper flattens by the same rule.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.ItemsOfKind(ProtoReportItemKinds.Coverage), Has.Exactly(3).Items);
            Assert.That(context.ForTarget("api"), Has.Exactly(3).Items);
            Assert.That(context.InCategory("GraphQL field"), Has.Exactly(2).Items);
            var warnings = context.WithStatus(ProtoReportStatus.Warning).ToArray();
            Assert.That(warnings, Has.Exactly(1).Items);
            Assert.That(warnings[0].Identifier, Is.EqualTo("Query.customers"));
        }
    }

    private sealed class TestCoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
    {
        public override string Category => "Endpoints";
    }

    private sealed class CapturingSink : IProtoSink
    {
        private ProtoReportItem[] _items = [];

        public IReadOnlyList<ProtoReportItem> Items => _items;

        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
        {
            _items = [.. items];
            return Task.CompletedTask;
        }
    }
}
