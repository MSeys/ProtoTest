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
    public async Task GateFindings_ShouldReachTheReport()
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

        // Assert
        var finding = sink.Items.Single(item => item.Category == "Gate");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Identifier, Is.EqualTo("coverage is complete"));
            Assert.That(finding.Kind, Is.EqualTo(ProtoReportItemKind.Finding));
            Assert.That(finding.Status, Is.EqualTo(ProtoReportStatus.Error));
            Assert.That(finding.Message, Is.EqualTo("Only half covered."));
            Assert.That(finding.Tags, Has.Count.EqualTo(2));
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
            new ProtoReportItem("api", "Endpoints", "GET /a", Kind: ProtoReportItemKind.Coverage, IsCovered: true),
            new ProtoReportItem("api", "Endpoints", "GET /b", Kind: ProtoReportItemKind.Coverage, IsCovered: false),
            new ProtoReportItem("api", "Endpoints", "POST /c", Kind: ProtoReportItemKind.Coverage, IsCovered: true),
            new ProtoReportItem("api", "Findings", "note", Kind: ProtoReportItemKind.Finding)
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
            Assert.That(context.ItemsOfKind(ProtoReportItemKind.Finding), Has.Exactly(1).Items);
        }
    }

    [Test]
    public void Context_ShouldExposeAnyCollectedEvidenceNotJustCoverage()
    {
        // Arrange: a collector reporting findings and metrics, with no coverage at all.
        var items = new[]
        {
            new ProtoReportItem("billing", "Findings", "orphaned invoices", Kind: ProtoReportItemKind.Finding, Status: ProtoReportStatus.Error),
            new ProtoReportItem("billing", "Metrics", "avg latency", Kind: ProtoReportItemKind.Metric, Status: ProtoReportStatus.Warning, Value: 812d, Unit: "ms")
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
