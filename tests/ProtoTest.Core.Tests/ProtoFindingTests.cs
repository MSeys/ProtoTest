namespace ProtoTest.Core.Tests;

using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

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
        var context = await host.StartTestAsync("Checkout", "00042", TestMethods.Placeholder);
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
        var context = await host.StartTestAsync("Checkout", "00043", TestMethods.Placeholder);
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
        var context = await host.StartTestAsync("Checkout", "00044", TestMethods.Placeholder);
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
    public async Task AddFinding_ShouldRedactSensitiveMetadataBeforeTheReport()
    {
        // Arrange
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        await using var host = builder.Build();
        await host.StartAsync();

        // Act
        var context = await host.StartTestAsync("Checkout", "00045", TestMethods.Placeholder);
        context.AddFinding(
            "A token leaked into the metadata.",
            metadata: new Dictionary<string, object>
            {
                ["token"] = "hunter2",
                ["details"] = new Dictionary<string, object>
                {
                    ["clientSecret"] = "s3cr3t",
                    ["region"] = "eu"
                }
            });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert
        var finding = sink.Items.Single(item => item.Kind == ProtoReportItemKinds.Finding);
        var details = (IReadOnlyDictionary<string, object>)finding.Metadata!["details"];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finding.Metadata!["token"], Is.EqualTo("[REDACTED]"));
            Assert.That(details["clientSecret"], Is.EqualTo("[REDACTED]"));
            Assert.That(details["region"], Is.EqualTo("eu"));
        }
    }

    [Test]
    public void AddFinding_ShouldRejectAnEmptyMessage()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", TestMethods.Placeholder);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => context.AddFinding("   "));
    }

    [Test]
    public async Task AddFinding_ShouldRedactSensitiveMetadataInTheTrace()
    {
        // One redacted copy serves the report item and the trace record.
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();

        // Act
        var context = await host.StartTestAsync("Checkout", "00046", TestMethods.Placeholder);
        context.AddFinding(
            "A token leaked into the metadata.",
            metadata: new Dictionary<string, object>
            {
                ["token"] = "hunter2"
            });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert
        var finding = host.Trace.Snapshot().Tests.Single().Record!.Findings!.Single();
        Assert.That(finding.Metadata!["token"], Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public async Task AddFinding_ShouldRedactNewSensitiveNamesInsideSequences()
    {
        // The shared list covers client_secret and id_token, and a sequence of dictionaries is
        // walked the same way a top-level dictionary is.
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();

        // Act
        var context = await host.StartTestAsync("Checkout", "00049", TestMethods.Placeholder);
        context.AddFinding(
            "Credentials leaked into the metadata.",
            metadata: new Dictionary<string, object>
            {
                ["credentials"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["client_secret"] = "s3cr3t",
                        ["id_token"] = "token-value",
                        ["region"] = "eu"
                    }
                }
            });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert
        var finding = host.Trace.Snapshot().Tests.Single().Record!.Findings!.Single();
        var credential = (IReadOnlyDictionary<string, object>)((IEnumerable<object?>)finding.Metadata!["credentials"]).Single()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(credential["client_secret"], Is.EqualTo("[REDACTED]"));
            Assert.That(credential["id_token"], Is.EqualTo("[REDACTED]"));
            Assert.That(credential["region"], Is.EqualTo("eu"));
        }
    }

    [Test]
    public async Task TeardownFailure_ShouldReachTheReportAndTheRunGate()
    {
        // A teardown failure is recorded through AddFinding, so it is
        // visible to sinks and run gates exactly like a failure the test reports itself. The test's own
        // result is still untouched.
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        builder.ConfigureServices(services => services.AddSingleton<IProtoTestHook>(new FailingTeardownHook()));
        builder.AddRunGate("no error findings", context =>
            context.WithStatus(ProtoReportStatus.Error).Any()
                ? ProtoRunGateResult.Failed("The run collected error findings.")
                : ProtoRunGateResult.Passed());
        var host = builder.Build();
        try
        {
            await host.StartAsync();

            // Act
            await host.StartTestAsync("Failure", "00047", TestMethods.Placeholder);
            Assert.CatchAsync(async () => await host.CompleteTestAsync(ProtoTestResult.Passed));

            // The gate sees the error finding and fails the run at shutdown; sinks still export.
            Assert.ThrowsAsync<ProtoRunGateException>(async () => await host.StopAsync());

            // Assert
            var traceFindings = host.Trace.Snapshot().Tests.Single().Record!.Findings!;
            var reportFinding = sink.Items.Single(item => item.Kind == ProtoReportItemKinds.Finding);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    traceFindings,
                    Has.Some.Matches<ProtoTraceFindingRecord>(finding =>
                        finding.Category == "Teardown" && finding.Message.Contains("Teardown failed")));
                Assert.That(reportFinding.Status, Is.EqualTo(ProtoReportStatus.Error));
                Assert.That(reportFinding.Message, Does.Contain("Teardown failed"));
                Assert.That(reportFinding.DisplayGroup, Is.EqualTo("Failure"));
            }
        }
        finally
        {
            // The stop already ran (and failed); disposal is clean because the run is stopped.
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AddFinding_WithCyclicMetadata_ShouldWriteASafeArchive()
    {
        // Cyclic metadata degrades to a marker instead of failing the
        // export; the archive is valid and the run stops cleanly.
        var output = Path.Combine(Path.GetTempPath(), $"prototest-cycle-{Guid.NewGuid():N}.prototrace");
        var metadata = new Dictionary<string, object>();
        metadata["self"] = metadata;
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("Cycle", "00048", TestMethods.Placeholder);
            context.AddFinding("Cyclic metadata.", metadata: metadata);
            await host.CompleteTestAsync(ProtoTestResult.Passed);

            // Act
            await host.StopAsync();

            // Assert
            Assert.That(File.Exists(output), Is.True, "the archive is written");
            using var archive = ZipFile.OpenRead(output);
            using var reader = new StreamReader(archive.GetEntry("spans.json")!.Open());
            using var spans = JsonDocument.Parse(await reader.ReadToEndAsync());
            Assert.That(spans.RootElement.GetRawText(), Does.Contain("[circular]"));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    private sealed class FailingTeardownHook : IProtoTestHook
    {
        public int Order => 10;

        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context)
            => Task.FromException(new InvalidOperationException("Teardown hook failed."));
    }

}
