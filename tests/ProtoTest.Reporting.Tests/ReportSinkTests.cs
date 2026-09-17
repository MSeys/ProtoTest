namespace ProtoTest.Reporting.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using System.Text.Json;

[TestFixture]
public sealed class ReportSinkTests
{
    [Test]
    public async Task JsonSink_ShouldWriteHierarchicalReportAndSummary()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.json");
        try
        {
            var sink = new JsonReportSink(new JsonReportSinkOptions { OutputPath = path });
            await sink.ExportAsync(SampleItems());

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var summary = json.RootElement.GetProperty("Summary");
            Assert.That(summary.GetProperty("Total").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("Covered").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("Warnings").GetInt32(), Is.EqualTo(1));
            Assert.That(json.RootElement.GetProperty("Items")[0].GetProperty("Status").GetString(), Is.EqualTo("Warning"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task HtmlSink_ShouldEncodeContentAndRenderSemanticStyling()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.html");
        try
        {
            var sink = new HtmlReportSink(new HtmlReportSinkOptions { OutputPath = path, Title = "Report <suite>" });
            await sink.ExportAsync(SampleItems());

            var html = await File.ReadAllTextAsync(path);
            Assert.That(html, Does.Contain("Report &lt;suite&gt;"));
            Assert.That(html, Does.Contain("status-warning"));
            Assert.That(html, Does.Contain("uncovered"));
            Assert.That(html, Does.Contain("Needs &lt;attention&gt;"));
            Assert.That(html, Does.Contain("id=\"reportSearch\""));
            Assert.That(html, Does.Contain("data-filter=\"uncovered\""));
            Assert.That(html, Does.Contain("data-filter=\"partial\""));
            Assert.That(html, Does.Contain("id=\"themeToggle\""));
            Assert.That(html, Does.Contain("class=\"coverage-ring\""));
            Assert.That(html, Does.Contain("data-search=\"get /orders"));
            Assert.That(html, Does.Contain("localStorage.getItem('prototest-report-theme')"));
            Assert.That(html, Does.Contain("REPORTING BLUEPRINT"));
            Assert.That(html, Does.Contain("<path fill=\"#123B58\" d=\"M301 263V300H383"));
            Assert.That(html, Does.Contain("data:image/svg+xml;base64,"));
            Assert.That(html, Does.Contain("--blueprint:#4eb7ee"));
            Assert.That(html, Does.Contain("background-size:28px 28px"));
            Assert.That(html, Does.Contain("scrollbar-color:var(--border-strong) transparent"));
            Assert.That(html, Does.Contain("*::-webkit-scrollbar-thumb:hover{background-color:var(--blueprint)}"));
            Assert.That(html, Does.Contain("class=\"report-item partial status-warning root\""));
            Assert.That(html, Does.Contain(">Partial</span>"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task HtmlSink_ShouldRenderLeavesWithoutDisclosureAndSimplifyOpenApiLabels()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.html");
        try
        {
            var items = new[]
            {
                new ProtoReportItem(
                    "Orders", "OpenAPI", "GET /orders/{id}",
                    ProtoReportItemKind.Coverage, ProtoReportStatus.Success, 1, true,
                    Children:
                    [
                        new ProtoReportItem(
                            "Orders", "OpenAPI Response", "200",
                            ProtoReportItemKind.Coverage, ProtoReportStatus.Success, 1, true,
                            Children:
                            [
                                new ProtoReportItem(
                                    "Orders", "OpenAPI Property", "$.address.city",
                                    ProtoReportItemKind.Coverage, ProtoReportStatus.Success, 1, true,
                                    DisplayName: "address › city", DisplayGroup: "Property")
                            ],
                            DisplayName: "200 response", DisplayGroup: "Response")
                    ])
            };
            var sink = new HtmlReportSink(new HtmlReportSinkOptions { OutputPath = path });

            await sink.ExportAsync(items);

            var html = await File.ReadAllTextAsync(path);
            Assert.That(html, Does.Contain(">200 response</strong>"));
            Assert.That(html, Does.Contain(">address › city</strong>"));
            Assert.That(html, Does.Contain("title=\"$.address.city\""));
            Assert.That(html, Does.Contain("<article class=\"report-item covered status-success child\""));
            Assert.That(html, Does.Not.Contain("GET /orders/{id} -&gt; 200"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task AddSink_ShouldExportRegisteredReportSourcesWhenRunStops()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "end-of-run.json");
        var tracePath = Path.Combine(directory, "run.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = tracePath);
            builder.ConfigureServices(services =>
                services.AddSingleton<IProtoReportSource>(new StubReportSource(SampleItems())));
            builder.AddSink<JsonReportSink>(sink => sink.OutputPath = path);
            await using var host = builder.Build();

            await host.StartAsync();
            Assert.That(File.Exists(path), Is.False);
            await host.StopAsync();
            Assert.That(File.Exists(path), Is.True);
            var artifact = host.Trace.Snapshot().Artifacts!.Single();
            Assert.Multiple(() =>
            {
                Assert.That(artifact.Name, Is.EqualTo("end-of-run.json"));
                Assert.That(artifact.MediaType, Is.EqualTo("application/json"));
                Assert.That(File.Exists(tracePath), Is.True);
            });
            using var archive = System.IO.Compression.ZipFile.OpenRead(tracePath);
            Assert.That(archive.GetEntry(artifact.ArchivePath), Is.Not.Null);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task ExportHook_ShouldNotDuplicateACollectorAlsoRegisteredAsReportSource()
    {
        var source = new StubCollectorAndReportSource(SampleItems());
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoCollector>(source);
            services.AddSingleton<IProtoReportSource>(source);
        });
        builder.AddSink(sink);
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.That(sink.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ExportHook_ShouldAttemptEverySinkAndAggregateFailures()
    {
        var first = new ThrowingSink();
        var second = new ThrowingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(first);
        builder.AddSink(second);
        await using var host = builder.Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<AggregateException>(async () => await host.StopAsync());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(first.CallCount, Is.EqualTo(1));
        Assert.That(second.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SinkConfiguration_ShouldOverrideCodeDefaultsFromKnownSection()
    {
        var directory = CreateTempDirectory();
        var codePath = Path.Combine(directory, "code.json");
        var configuredPath = Path.Combine(directory, "configured.json");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Reporting:Json:OutputPath"] = configuredPath,
                    ["ProtoTest:Reporting:Json:Indented"] = "false"
                }));
            builder.AddSink<JsonReportSink>(sink => sink.OutputPath = codePath);
            await using var host = builder.Build();

            await host.StartAsync();
            await host.StopAsync();

            Assert.That(File.Exists(configuredPath), Is.True);
            Assert.That(File.Exists(codePath), Is.False);
            Assert.That(await File.ReadAllTextAsync(configuredPath), Does.Not.Contain(Environment.NewLine));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ProtoReportItem[] SampleItems() =>
    [
        new(
            "Orders", "REST", "GET /orders",
            Kind: ProtoReportItemKind.Coverage,
            Status: ProtoReportStatus.Warning,
            Count: 2,
            IsCovered: true,
            Message: "Needs <attention>",
            Tags: ["contract"],
            Children:
            [
                new("Orders", "Status", "200", ProtoReportItemKind.Coverage, ProtoReportStatus.Success, 2, true),
                new("Orders", "Status", "404", ProtoReportItemKind.Coverage, IsCovered: false)
            ])
    ];

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ProtoTest.Reporting.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubReportSource(IEnumerable<ProtoReportItem> items) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems() => items;
    }

    private sealed class StubCollectorAndReportSource(IEnumerable<ProtoReportItem> items)
        : IProtoCollector, IProtoReportSource
    {
        public bool CanCollect(ProtoObservation observation) => true;
        public void Collect(ProtoObservation observation) { }
        public IEnumerable<ProtoReportItem> GetReportItems() => items;
    }

    private sealed class CapturingSink : IProtoSink
    {
        public IReadOnlyList<ProtoReportItem> Items { get; private set; } = [];

        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
        {
            Items = [.. items];
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IProtoSink
    {
        public int CallCount { get; private set; }

        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException(new InvalidOperationException("Export failed."));
        }
    }
}
