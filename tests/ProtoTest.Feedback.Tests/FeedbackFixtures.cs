namespace ProtoTest.Feedback.Tests;

using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoTest.Core;
using ProtoTest.Diagnosis;
using ProtoTest.Reporting;
using ProtoTest.Traces;

/// <summary>
/// The shared test inputs: the committed traces the diagnosis fixtures also use, plus hand-built
/// digests so a channel's exact rendering is pinned without depending on a generated source line.
/// </summary>
internal static class FeedbackFixtures
{
    /// <summary>Every environment variable the feedback CLI reads; cleared before a target test runs.</summary>
    public static readonly string[] TargetEnvironmentVariables =
    [
        "GITHUB_TOKEN",
        "GITHUB_REPOSITORY",
        "GITHUB_EVENT_PATH",
        "GITHUB_API_URL",
        "PROTOTEST_FEEDBACK_TRACE_URL",
        "PROTOTEST_FEEDBACK_WEBHOOK_URL",
        "PROTOTEST_FEEDBACK_WEBHOOK_SECRET",
        "PROTOTEST_FEEDBACK_WEBHOOK_SECRET_HEADER",
        "PROTOTEST_FEEDBACK_CARD_URL",
        "GITHUB_SERVER_URL",
        "GITHUB_SHA"
    ];

    /// <summary>One committed MCP fixture trace, written by the framework and reused here.</summary>
    public static string McpFixture(string name)
        => RepositoryFile("tests", "ProtoTest.Mcp.Tests", "Fixtures", $"{name}.prototrace");

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

    /// <summary>A digest with one failed test, optionally a failed gate and coverage.</summary>
    public static ProtoDiagnosisDocument FailedDigest(
        string testName = "orders match their shape",
        string errorMessage = "Shape mismatch failed with 1 error(s).",
        string? sourceFile = "tests/Orders/OrderTests.cs",
        int? sourceLine = 42,
        string outcome = "failed",
        bool failedGate = false,
        bool withCoverage = true)
    {
        var failure = new ProtoDiagnosisOperation(
            "5", "4", "assert.json.shape", "assert.json.shape", "Fixture", "execution", "failed",
            "ProtoTest.Json.JsonShapeMismatchException", errorMessage,
            sourceFile, sourceLine, "FixtureMethods.FailingOrderShape",
            null, null, null, null, false);
        var test = new ProtoDiagnosedTest(
            "00002", testName, "FixtureMethods", "FailingOrderShape", outcome, 16.4,
            failure, ProtoDiagnosisRule.Assertion, null,
            [
                new ProtoTraceMismatch(
                    "$.orderId", "Values did not match.",
                    JsonSerializer.Deserialize<JsonElement>("7"),
                    JsonSerializer.Deserialize<JsonElement>("42"))
            ],
            false, [], false, [], false);

        return new ProtoDiagnosisDocument(
            ProtoDiagnosisDocument.CurrentDigestVersion,
            "2.0",
            "run-1",
            "TestResults/run-1.prototrace",
            DateTimeOffset.Parse("2026-09-28T09:55:33+00:00", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-09-28T09:55:36+00:00", System.Globalization.CultureInfo.InvariantCulture),
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["ci"] = "true" },
            new Dictionary<string, int>(StringComparer.Ordinal) { [outcome] = 1 },
            [test],
            failedGate
                ? [new ProtoDiagnosisGate("coverage gate", "failed", "Coverage regressed below the agreed floor.", [])]
                : [],
            false,
            [],
            false,
            withCoverage
                ? new ProtoDiagnosisCoverage(20, 12, 8, 60, "report.json", "resources/run/report.json", 100)
                : null,
            withCoverage ? null : "No JSON report artifact in this run.");
    }

    /// <summary>A green digest: one succeeded test, no gates, coverage published.</summary>
    public static ProtoDiagnosisDocument GreenDigest()
    {
        var document = FailedDigest(outcome: "succeeded");
        return document with
        {
            Failures = [],
            Outcomes = new Dictionary<string, int>(StringComparer.Ordinal) { ["succeeded"] = 1 }
        };
    }

    /// <summary>Writes two report files: the baseline covers a unit the current run leaves uncovered.</summary>
    public static (string Baseline, string Current) RegressedReports(string directory)
        => (
            WriteReport(
                Path.Combine(directory, "baseline.json"),
                Unit("Northstar:Api", "OpenAPI", "GET /api/v1/orders", covered: true)),
            WriteReport(
                Path.Combine(directory, "current.json"),
                Unit("Northstar:Api", "OpenAPI", "GET /api/v1/orders", covered: false))
        );

    /// <summary>Writes a report the way a reporting sink writes one.</summary>
    public static string WriteReport(string path, params ProtoReportItem[] items)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(ProtoReport.Create(items), options));
        return path;
    }

    /// <summary>One coverage unit, the shape the report arithmetic counts.</summary>
    public static ProtoReportItem Unit(string target, string category, string identifier, bool covered)
        => new(target, category, identifier,
            Kind: ProtoReportItemKinds.Coverage,
            Status: covered ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
            Count: covered ? 1 : 0,
            IsCovered: covered);

    /// <summary>A unique directory under the temp path, deleted with the test.</summary>
    public static string NewTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Runs an action with every feedback target variable cleared, then applies overrides.</summary>
    public static void WithEnvironment(IReadOnlyDictionary<string, string?> overrides, Action act)
    {
        var before = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in TargetEnvironmentVariables)
        {
            before[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, overrides.GetValueOrDefault(name));
        }

        try
        {
            act();
        }
        finally
        {
            foreach (var (name, value) in before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    /// <summary>Runs a function under <see cref="WithEnvironment"/> and returns its result.</summary>
    public static T WithEnvironmentResult<T>(IReadOnlyDictionary<string, string?> overrides, Func<T> act)
    {
        var result = default(T)!;
        WithEnvironment(overrides, () => result = act());
        return result;
    }
}
