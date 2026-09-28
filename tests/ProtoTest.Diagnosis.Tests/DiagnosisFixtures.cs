namespace ProtoTest.Diagnosis.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Reporting;
using ProtoTest.TestSupport;

/// <summary>
/// Writes small real traces through the host builder, one per diagnosis rule, so the tests exercise
/// the public entry points and the archive round trip instead of a hand-built model.
/// </summary>
internal static class DiagnosisFixtures
{
    /// <summary>The trace source the fixtures record their operations under.</summary>
    public const string Source = "ProtoTest.Diagnosis.Tests";

    /// <summary>The recorded request identifier the fixtures' call operations carry.</summary>
    public const string RequestIdentifier = "GET /api/v1/orders";

    /// <summary>A failed shape assertion with recorded mismatches, a checks section, an artifact and state.</summary>
    public static async Task WriteAssertionFailureAsync(string path, bool withReport = false)
    {
        var builder = NewHost(path, withReport);
        if (withReport)
        {
            builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new CoverageReportSource()));
        }

        await using var host = builder.Build();
        await host.StartAsync();
        var failing = await host.StartTestAsync("orders match their shape", "00002", TestMethods.Placeholder);
        using (var request = failing.Trace.Operation("http.request", "REST · GET orders", Source)
            .With("request.identifier", RequestIdentifier)
            .Begin())
        {
            using var check = failing.Trace.Operation("assert.json.shape", "assert.json.shape", Source)
                .With("shape.mismatches", """[{"propertyPath":"$.orderId","reason":"Values did not match.","expected":7,"actual":42}]""")
                .With("shape.mismatch_count", "1")
                .Begin();
            check.AddSection(new ProtoTraceSection(
                "Result",
                ProtoTraceSectionKind.Checks,
                [new ProtoTraceSectionItem("shape", "1 mismatch", "Values did not match.", ProtoTraceSectionTone.Error)]));
            failing.Trace.Value(
                "value",
                "order:order-1",
                "Order order-1",
                "changed",
                new Dictionary<string, string?> { ["order.total"] = "42" });
            failing.AddAttachment("response.json", """{"orderId":42}""", "application/json", "The response that failed the shape.");
            check.Fail(new InvalidOperationException("Shape mismatch failed with 1 error(s)."));
        }

        failing.AddAttachment("payload.txt", new string('x', 100_000), "text/plain", "An oversized text payload.");
        await host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException("Shape mismatch failed with 1 error(s).")));
        await host.StopAsync();
    }

    /// <summary>A protocol call that failed with an error, nested under no wider operation.</summary>
    public static async Task WriteOperationErrorAsync(string path)
    {
        await using var host = NewHost(path).Build();
        await host.StartAsync();
        var failing = await host.StartTestAsync("orders are listed", "00001", TestMethods.Placeholder);
        failing.Trace.SetEntityState(
            "client",
            "client:Rest:Orders",
            "Orders",
            new Dictionary<string, string?> { ["resource.state"] = "registered" });
        using (var request = failing.Trace.Operation("http.request", "REST · GET orders", Source)
            .With("request.identifier", RequestIdentifier)
            .For("client", "client:Rest:Orders")
            .Begin())
        {
            request.Fail(new TimeoutException("The API did not answer within 2 seconds."));
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(new TimeoutException("The API did not answer within 2 seconds.")));
        await host.StopAsync();
    }

    /// <summary>A runner-reported failure with no recorded operation at all.</summary>
    public static async Task WriteRunnerFailureAsync(string path)
    {
        await using var host = NewHost(path).Build();
        await host.StartAsync();
        await host.StartTestAsync("the runner reports a failure", "00003", TestMethods.Placeholder);
        await host.CompleteTestAsync(ProtoTestResult.Failed(
            "NUnit",
            "Failed",
            "The runner reported a failure without an exception."));
        await host.StopAsync();
    }

    /// <summary>A partial test whose only explanation is a recorded finding.</summary>
    public static async Task WriteFindingAsync(string path, bool withReport = false)
    {
        await using var host = NewHost(path, withReport).Build();
        await host.StartAsync();
        var partial = await host.StartTestAsync("orders are listed", "00004", TestMethods.Placeholder);
        using (partial.Trace.Operation("http.request", "REST · GET orders", Source)
            .With("request.identifier", RequestIdentifier)
            .Begin())
        {
        }

        partial.AddFinding("Teardown failed: the database connection was already closed.", ProtoReportStatus.Error, category: "Teardown");
        await host.CompleteTestAsync(ProtoTestResult.Partial);
        await host.StopAsync();
    }

    /// <summary>A green run whose run gate failed, with the report when a sink is registered.</summary>
    public static async Task WriteGateFailureAsync(string path, bool withReport = false)
    {
        var builder = NewHost(path, withReport);
        builder.AddRunGate("coverage gate", _ => ProtoRunGateResult.Failed(
            "Coverage regressed below the agreed floor.",
            [RequestIdentifier, "POST /api/v1/orders"]));
        await using var host = builder.Build();
        await host.StartAsync();
        var green = await host.StartTestAsync("orders are listed", "00005", TestMethods.Placeholder);
        using (green.Trace.Operation("http.request", "REST · GET orders", Source)
            .With("request.identifier", RequestIdentifier)
            .Begin())
        {
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        try
        {
            await host.StopAsync();
        }
        catch (ProtoRunGateException)
        {
        }
    }

    /// <summary>A run oversized on every axis the caps bound, with a report of many uncovered units.</summary>
    public static async Task WriteNoisyAsync(string path, bool withReport)
    {
        var builder = NewHost(path, withReport);
        if (withReport)
        {
            builder.ConfigureServices(services => services.AddSingleton<IProtoReportSource>(new NoisyReportSource(uncoveredUnits: 1000)));
        }

        await using var host = builder.Build();
        await host.StartAsync();
        var failing = await host.StartTestAsync("a noisy failure", "00006", TestMethods.Placeholder);
        using (var operation = failing.Trace.Operation("http.request", "REST · POST noisy", Source)
            .With("request.identifier", "POST /api/v1/noisy")
            .Begin())
        {
            operation.AddSection(new ProtoTraceSection(
                "Body",
                ProtoTraceSectionKind.Code,
                Items: null,
                Content: new string('x', 200_000),
                Language: "json"));
            operation.SetAttribute("shape.mismatches", NoisyMismatches(count: 100));
            for (var index = 0; index < 20; index++)
            {
                failing.Trace.Value("value", $"value:{index}", $"Value {index}", "changed");
            }

            operation.Fail(new TimeoutException(new string('x', 200_000)));
        }

        for (var index = 0; index < 25; index++)
        {
            failing.AddAttachment($"noisy-{index}.json", """{"index":0}""", "application/json");
        }

        for (var index = 0; index < 20; index++)
        {
            failing.AddFinding($"Finding {index}.", ProtoReportStatus.Warning, category: "Noisy");
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException(new string('x', 200_000))));
        await host.StopAsync();
    }

    /// <summary>Runs a fixture into a temporary trace and hands the file to the test.</summary>
    public static async Task WithTraceAsync(Func<string, Task> act, Func<string, Task> write)
    {
        using var trace = new TemporaryTrace("diagnosis");
        await write(trace.Path);
        await act(trace.Path);
    }

    /// <summary>A committed file under the repository root, located from the test output directory.</summary>
    public static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProtoTest.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found from the test output.");
        return Path.Combine([root, .. segments]);
    }

    /// <summary>One committed MCP fixture trace.</summary>
    public static string McpFixture(string name)
        => RepositoryFile("tests", "ProtoTest.Mcp.Tests", "Fixtures", $"{name}.prototrace");

    private static string NoisyMismatches(int count)
    {
        var entries = Enumerable.Range(0, count)
            .Select(index => $$"""{"propertyPath":"$.field{{index}}","reason":"Values did not match.","expected":{{index}},"actual":{{index + 1}}}""");
        return $"[{string.Join(",", entries)}]";
    }

    private static ProtoHostBuilder NewHost(string path, bool withReport = false)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = path);
        if (withReport)
        {
            builder.AddSink(new JsonReportSink
            {
                OutputPath = $"{Path.ChangeExtension(path, null)}.report.json",
                Indented = false
            });
        }

        return builder;
    }

    private sealed class CoverageReportSource : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems()
            =>
            [
                new(
                    "Northstar:Api",
                    "OpenAPI",
                    RequestIdentifier,
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: ProtoReportStatus.Success,
                    Count: 2,
                    IsCovered: true),
                new(
                    "Northstar:Api",
                    "OpenAPI Property",
                    "$.orderId",
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: ProtoReportStatus.Neutral,
                    Count: 0,
                    IsCovered: false)
            ];
    }

    private sealed class NoisyReportSource(int uncoveredUnits) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems()
            => Enumerable.Range(0, uncoveredUnits).Select(index => new ProtoReportItem(
                "Noisy:Api",
                "OpenAPI Property",
                $"$.field{index}",
                Kind: ProtoReportItemKinds.Coverage,
                IsCovered: false));
    }
}
