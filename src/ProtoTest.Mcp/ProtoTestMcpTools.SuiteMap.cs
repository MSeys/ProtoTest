namespace ProtoTest.Mcp;

using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProtoTest.Traces;

public sealed partial class ProtoTestMcpTools
{
    private const int MaxMapEntries = 50;
    private const int MaxMapGaps = 50;

    /// <summary>
    /// The suite a test is written into, as the newest run shows it: what the host composed, the clients,
    /// provisioners, attributes, pages and devices the tests used, one example test per kind of work, and
    /// the coverage gaps still open.
    /// </summary>
    [McpServerTool(
        Name = "get_suite_map",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Describes the suite a new test is written into, from one run: the capabilities and infrastructure the " +
        "host composed, the clients, data provisioners, attributes, page objects and devices the tests used, " +
        "one example test per kind of work with its source file, and the coverage gaps still open. Everything " +
        "is what the run recorded, so a provisioner or page no test used is not listed. Read-only.")]
    public string GetSuiteMap(
        [Description("Run id to read; defaults to the newest discovered run.")]
        string? runId = null)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var run = McpRunDiscovery.ResolveRun(discovered, runId);
        var archive = run.Archive;
        ProtoTraceState state;
        try
        {
            state = archive.ReadState();
        }
        catch (InvalidDataException exception)
        {
            throw new McpException(exception.Message);
        }

        var operations = archive.Tests
            .SelectMany(test => test.Operations.Select(operation => (Test: test, Operation: operation)))
            .ToArray();

        var capabilities = state.RunItems
            .Where(item => item.Kind == "capability")
            .Select(item => new
            {
                kind = Read(item.State, "capability.kind"),
                name = Read(item.State, "capability.name"),
                instance = Read(item.State, "capability.instance"),
                package = Read(item.State, "capability.source")
            });
        var infrastructure = state.RunItems
            .Where(item => Read(item.State, "infrastructure.kind") is not null)
            .Select(item => new
            {
                kind = Read(item.State, "infrastructure.kind"),
                id = item.Id,
                description = Read(item.State, "resource.description"),
                settings = Read(item.State, "infrastructure.settings")
            });

        var clients = operations
            .Where(entry => entry.Operation.Kind == "client.initialize")
            .GroupBy(entry => (Protocol: Read(entry.Operation.Attributes, "client.protocol"), Name: Read(entry.Operation.Attributes, "client.name")))
            .Select(group => new
            {
                protocol = group.Key.Protocol,
                name = group.Key.Name,
                type = Read(group.First().Operation.Attributes, "client.type")
            });
        var provisioners = operations
            .Where(entry => entry.Operation.Kind == "data.provision")
            .GroupBy(entry => Read(entry.Operation.Attributes, "data.provisioner"))
            .Select(group => new
            {
                provisioner = group.Key,
                input = Read(group.First().Operation.Attributes, "data.input_type"),
                result = Read(group.First().Operation.Attributes, "data.result_type"),
                owned = Read(group.First().Operation.Attributes, "data.owned") == "true",
                usedByTests = group.Select(entry => entry.Test.TestId).Distinct(StringComparer.Ordinal).Count()
            });
        var attributes = operations
            .Where(entry => entry.Operation.Kind == "attribute.before")
            .GroupBy(entry => Read(entry.Operation.Attributes, "attribute.type"))
            .Select(group => new
            {
                type = group.Key,
                suite = group.Key is not null && !group.Key.StartsWith("ProtoTest.", StringComparison.Ordinal),
                usedByTests = group.Select(entry => entry.Test.TestId).Distinct(StringComparer.Ordinal).Count()
            })
            .OrderByDescending(attribute => attribute.suite);
        var pages = operations
            .Where(entry => entry.Operation.Kind.StartsWith("web.", StringComparison.Ordinal) || entry.Operation.Kind == "assert.web")
            .Select(entry => (Component: Read(entry.Operation.Attributes, "web.component"), Element: Read(entry.Operation.Attributes, "web.element"), Locator: Read(entry.Operation.Attributes, "web.locator")))
            .Where(entry => entry.Component is not null)
            .GroupBy(entry => KeyedSegment().Replace(entry.Component!.Split('.', 2)[0], "[key]"), StringComparer.Ordinal)
            .Select(group => new
            {
                page = group.Key,
                elements = group
                    .Where(entry => entry.Element is not null)
                    .Select(entry => new { path = ElementPath(entry.Component!, entry.Element!), locator = entry.Locator })
                    .DistinctBy(entry => entry.path)
                    .Take(MaxMapEntries)
            });
        var devices = state.Tests
            .SelectMany(test => test.Items)
            .Where(item => item.Kind == "device")
            .GroupBy(item => (Client: Read(item.State, "device.client"), Type: Read(item.State, "device.type")))
            .Select(group => new
            {
                client = group.Key.Client,
                type = group.Key.Type,
                transport = Read(group.First().State, "device.transport")
            });

        var examples = ExampleKinds
            .Select(kind =>
            {
                var test = archive.Tests.FirstOrDefault(candidate => candidate.Succeeded && candidate.Operations.Any(operation => kind.Matches(operation.Kind)));
                return test is null
                    ? null
                    : new { work = kind.Work, test = test.Name, testClass = test.ClassName, method = test.MethodName, sourceFile = TestSourceFile(test) };
            })
            .Where(example => example is not null);

        object? gaps = null;
        string? gapsNote = null;
        try
        {
            if (ProtoTraceReport.TryRead(archive, out var report, out var reason))
            {
                var uncovered = report.Flatten()
                    .Where(item => string.Equals(item.Kind, "coverage", StringComparison.OrdinalIgnoreCase) && item.IsCovered is false)
                    .ToArray();
                gaps = new
                {
                    total = uncovered.Length,
                    units = uncovered.Take(MaxMapGaps).Select(item => new
                    {
                        target = item.TargetName,
                        category = item.Category,
                        identifier = item.Identifier
                    }),
                    truncated = uncovered.Length > MaxMapGaps
                };
            }
            else
            {
                gapsNote = reason;
            }
        }
        catch (InvalidDataException exception)
        {
            gapsNote = exception.Message;
        }

        return Json(new
        {
            runId = archive.RunId,
            traceFile = run.TraceFile,
            tests = archive.Tests.Count,
            capabilities = Capped(capabilities),
            infrastructure = Capped(infrastructure),
            clients = Capped(clients),
            provisioners = Capped(provisioners),
            attributes = Capped(attributes),
            pages = Capped(pages),
            devices = Capped(devices),
            examples = Capped(examples),
            coverageGaps = gaps,
            coverageNote = gapsNote,
            note = "Lists what this run recorded. Reuse these before writing new setup; read the example tests for the suite's style."
        });
    }

    private static readonly (string Work, Func<string, bool> Matches)[] ExampleKinds =
    [
        ("REST call", kind => kind == "http.request"),
        ("GraphQL call", kind => kind.StartsWith("graphql.", StringComparison.Ordinal)),
        ("gRPC call", kind => kind.StartsWith("grpc.", StringComparison.Ordinal)),
        ("browser journey", kind => kind.StartsWith("web.", StringComparison.Ordinal)),
        ("message", kind => kind.StartsWith("message.", StringComparison.Ordinal) || kind.StartsWith("messaging.", StringComparison.Ordinal)),
        ("database read", kind => kind.StartsWith("sql.query", StringComparison.Ordinal) || kind.StartsWith("sql.read", StringComparison.Ordinal)),
        ("device", kind => kind.StartsWith("device.", StringComparison.Ordinal)),
        ("test data", kind => kind == "data.provision"),
        ("test clock", kind => kind == "clock.advance")
    ];

    // The file of a call the test method itself made; an attribute or helper file would mislead.
    private static string? TestSourceFile(ProtoTraceTest test)
        => test.Operations
            .FirstOrDefault(operation => operation.SourceFile is not null
                && Read(operation.Attributes, "code.function.name") is { } function
                && function.Contains(test.MethodName, StringComparison.Ordinal))
            ?.SourceFile;

    // A keyed element (a row, a card) records the key it was found by; the map lists its shape.
    private static string ElementPath(string component, string element)
        => KeyedSegment().Replace($"{component}.{element}", "[key]");

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex KeyedSegment();

    private static object Capped<T>(IEnumerable<T> entries)
    {
        var page = entries.Take(MaxMapEntries + 1).ToArray();
        return new { entries = page.Take(MaxMapEntries), truncated = page.Length > MaxMapEntries };
    }

    private static string? Read(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
