namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

[TestFixture]
[NonParallelizable]
public class ProtoFindingTests
{
    [Test]
    public async Task AddFinding_ShouldReachTheRunReport()
    {
        // Arrange
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        await using var host = builder.Build();
        await host.StartAsync();

        // Act
        var context = await host.StartTestAsync("Checkout", "00042", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        context.AddFinding("Orphaned invoices were left behind.", ProtoReportStatus.Error, category: "Data isolation");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert
        var finding = sink.Items.Single(item => item.Kind == ProtoReportItemKinds.Finding);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Message, Is.EqualTo("Orphaned invoices were left behind."));
            Assert.That(finding.Status, Is.EqualTo(ProtoReportStatus.Error));
            Assert.That(finding.Category, Is.EqualTo("Data isolation"));
            Assert.That(finding.DisplayGroup, Is.EqualTo("Checkout"));
            Assert.That(finding.Metadata!["test.id"], Is.EqualTo("00042"));
        }
    }

    [Test]
    public async Task AddFinding_ShouldBeVisibleToRunGates()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddRunGate("no error findings", context =>
            context.WithStatus(ProtoReportStatus.Error).Any()
                ? ProtoRunGateResult.Failed("The run collected error findings.")
                : ProtoRunGateResult.Passed());
        await using var host = builder.Build();
        await host.StartAsync();

        // Act
        var context = await host.StartTestAsync("Checkout", "00043", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        context.AddFinding("Something is off.", ProtoReportStatus.Error);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var exception = Assert.ThrowsAsync<ProtoRunGateException>(() => host.StopAsync());

        // Assert
        Assert.That(exception!.Failures.Single().Name, Is.EqualTo("no error findings"));
    }

    [Test]
    public async Task AddFinding_ShouldAppearInTheTrace()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();

        // Act
        var context = await host.StartTestAsync("Checkout", "00044", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        context.AddFinding("Worth a look.", ProtoReportStatus.Warning, tags: ["baseline"]);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert
        var finding = host.Trace.Snapshot().Tests.Single().Record!.Findings!.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Status, Is.EqualTo("Warning"));
            Assert.That(finding.Message, Is.EqualTo("Worth a look."));
            Assert.That(finding.Tags, Is.EqualTo(new[] { "baseline" }));
        }
    }

    [Test]
    public void AddFinding_ShouldRejectAnEmptyMessage()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => context.AddFinding("   "));
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
