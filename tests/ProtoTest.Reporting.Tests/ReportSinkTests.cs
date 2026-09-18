namespace ProtoTest.Reporting.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using System.Security.Cryptography;
using System.Text;
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
            // Gate verdicts are their own kind, so they never count as findings.
            Assert.That(summary.GetProperty("Findings").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("Gates").GetInt32(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("Items")[0].GetProperty("Status").GetString(), Is.EqualTo("Warning"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task Summary_ShouldCountCategoriesAndObservedOccurrencesOnly()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.json");
        try
        {
            var items = new[]
            {
                new ProtoReportItem("Orders", "REST", "GET /orders",
                    ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, Count: 4, IsCovered: true),
                new ProtoReportItem("Test findings", "Delivery", "finding-001",
                    ProtoReportItemKinds.Finding, ProtoReportStatus.Warning, Message: "Slow deployment."),
                new ProtoReportItem("Run gates", "Gate", "no error findings",
                    ProtoReportItemKinds.Gate, ProtoReportStatus.Success, Count: 1, Message: "No errors found.")
            };
            var sink = new JsonReportSink(new JsonReportSinkOptions { OutputPath = path });

            await sink.ExportAsync(items);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var summary = json.RootElement.GetProperty("Summary");
            using (Assert.EnterMultipleScope())
            {
                // A gate verdict happens once; it is not an observed occurrence.
                Assert.That(summary.GetProperty("TotalOccurrences").GetInt32(), Is.EqualTo(4));
                Assert.That(summary.GetProperty("Findings").GetInt32(), Is.EqualTo(1));
                Assert.That(summary.GetProperty("Gates").GetInt32(), Is.EqualTo(1));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task HtmlSink_ShouldKeepReportCategoriesApartAndLabelGateVerdicts()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.html");
        try
        {
            var items = new[]
            {
                new ProtoReportItem("Orders", "OpenAPI", "GET /orders",
                    ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, Count: 4, IsCovered: true),
                new ProtoReportItem("Test findings", "Delivery", "finding-001",
                    ProtoReportItemKinds.Finding, ProtoReportStatus.Warning, Message: "Slow deployment."),
                new ProtoReportItem("Run gates", "Gate", "no error findings",
                    ProtoReportItemKinds.Gate, ProtoReportStatus.Success, Count: 1, Message: "No errors found."),
                new ProtoReportItem("Test resources", "Resources", "database:connection",
                    ProtoReportItemKinds.Resource, ProtoReportStatus.Success, Message: "SqliteConnection · None")
            };
            var sink = new HtmlReportSink(new HtmlReportSinkOptions { OutputPath = path });

            await sink.ExportAsync(items);

            var html = await File.ReadAllTextAsync(path);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(html, Does.Contain("data-report-section data-kind=\"coverage\""));
                Assert.That(html, Does.Contain("data-report-section data-kind=\"finding\""));
                Assert.That(html, Does.Contain("data-report-section data-kind=\"gate\""));
                Assert.That(html, Does.Contain("data-report-section data-kind=\"resource\""));
                Assert.That(html, Does.Contain(">Coverage</h3>"));
                Assert.That(html, Does.Contain(">Findings</h3>"));
                Assert.That(html, Does.Contain(">Run gates</h3>"));
                Assert.That(html, Does.Contain(">Resources</h3>"));
                // A passed gate reads as passed, not as a finding with a success status.
                Assert.That(html, Does.Contain("status-badge\">Passed</span>"));
                Assert.That(html, Does.Contain("1 entry"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task HtmlSink_ShouldGiveAnUnknownKindItsOwnSection()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.html");
        try
        {
            // An integration's own kind is not dropped; it gets a section titled after the kind.
            var items = new[]
            {
                new ProtoReportItem("Message broker", "Queues", "orders.created",
                    Kind: "broker.message", Status: ProtoReportStatus.Neutral, Message: "A message was observed.")
            };
            var sink = new HtmlReportSink(new HtmlReportSinkOptions { OutputPath = path });

            await sink.ExportAsync(items);

            var html = await File.ReadAllTextAsync(path);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(html, Does.Contain("data-report-section data-kind=\"broker.message\""));
                Assert.That(html, Does.Contain(">Broker message</h3>"));
                Assert.That(html, Does.Contain("A message was observed."));
            }
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
            // The report carries the shared design tokens rather than a palette of its own, so the
            // report, the ProtoTrace viewer and the documentation cannot drift apart.
            Assert.That(html, Does.Contain("--pt-blue-bright: #1688bf"));
            Assert.That(html, Does.Contain("--phase-execution: var(--pt-cyan)"));
            Assert.That(html, Does.Contain("background-size: var(--grid-size) var(--grid-size)"));
            // One brand mark, coloured by the surface like the viewer's, instead of navy on every theme.
            Assert.That(html, Does.Contain("fill: var(--brand-mark-body)"));
            Assert.That(html, Does.Contain("fill: var(--brand-mark-check)"));
            // The viewer's expander rather than a rotating glyph.
            Assert.That(html, Does.Contain("<path class=\"stem\""));
            // The empty state is shown by clearing its hidden attribute; its own display must not override that.
            Assert.That(html, Does.Contain(".empty-state[hidden] { display: none; }"));
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
                    ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, 1, true,
                    Children:
                    [
                        new ProtoReportItem(
                            "Orders", "OpenAPI Response", "200",
                            ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, 1, true,
                            Children:
                            [
                                new ProtoReportItem(
                                    "Orders", "OpenAPI Property", "$.address.city",
                                    ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, 1, true,
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

    [Test]
    public async Task ViewerContentSecurityPolicy_ShouldAllowTheReportScript()
    {
        // The viewer hosts the report in a sandboxed blob iframe, which inherits the viewer's policy. An
        // inline script only runs there when its hash is listed, so this hashes the rendered report itself:
        // it fails on a stale hash and on a script that is no longer inside a script element.
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "report.html");
        try
        {
            var sink = new HtmlReportSink(new HtmlReportSinkOptions { OutputPath = path });
            await sink.ExportAsync(SampleItems());

            var html = await File.ReadAllTextAsync(path);
            var open = html.IndexOf("<script>", StringComparison.Ordinal);
            var close = html.IndexOf("</script>", open + 1, StringComparison.Ordinal);
            Assert.That(open, Is.GreaterThanOrEqualTo(0), "The report must carry its script inside a script element.");
            Assert.That(close, Is.GreaterThan(open), "The report's script element must be closed.");

            var script = html[(open + "<script>".Length)..close].Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));

            var headers = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "viewer", "public", "_headers"));

            Assert.That(headers, Does.Contain($"'sha256-{hash}'"),
                "Add the report script's hash to the viewer's Content-Security-Policy, or the report preview renders without JavaScript.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProtoTest.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found from the test output.");
    }

    private static ProtoReportItem[] SampleItems() =>
    [
        new(
            "Orders", "REST", "GET /orders",
            Kind: ProtoReportItemKinds.Coverage,
            Status: ProtoReportStatus.Warning,
            Count: 2,
            IsCovered: true,
            Message: "Needs <attention>",
            Tags: ["contract"],
            Children:
            [
                new("Orders", "Status", "200", ProtoReportItemKinds.Coverage, ProtoReportStatus.Success, 2, true),
                new("Orders", "Status", "404", ProtoReportItemKinds.Coverage, IsCovered: false)
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
