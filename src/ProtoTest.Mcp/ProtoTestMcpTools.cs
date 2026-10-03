namespace ProtoTest.Mcp;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProtoTest.Diagnosis;
using ProtoTest.Traces;

/// <summary>
/// The read-only tools one MCP server exposes over the runs it can see. Every tool reads archives and
/// reports and returns a compact JSON document; none of them writes, reruns or mutates anything. The
/// hard caps live here, not in a transport, so the stdio host and the demo endpoint return the same
/// bounded shape.
/// </summary>
[McpServerToolType]
public sealed partial class ProtoTestMcpTools(ProtoTestMcpOptions options)
{
    private const int DefaultRunLimit = 10;
    private const int MaxRunLimit = 50;
    private const int MaxFailingTestsPerRun = 20;
    private const int MaxFailedOperations = 10;
    private const int MaxMismatches = 25;
    private const int MaxArtifactsPerTest = 20;
    private const int MaxErrorMessageCharacters = 4000;
    private const int DefaultUncoveredLimit = 50;
    private const int MaxUncoveredLimit = 200;
    private const int MaxSuggestions = 20;

    /// <summary>Lists the runs this server can see, newest first.</summary>
    [McpServerTool(
        Name = "list_runs",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Lists the ProtoTest runs this server can see, newest first, with each run's id, trace file, " +
        "start and completion time, outcome counts and failing test ids. Read-only.")]
    public string ListRuns(
        [Description("A folder to discover .prototrace archives in; defaults to the server's configured project folder. Not accepted by a server configured with a single trace file.")]
        string? folder = null,
        [Description("Maximum number of runs to return, 1-50; defaults to 10.")]
        int limit = DefaultRunLimit)
    {
        ProtoTraceFolderRuns discovered;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (!string.IsNullOrWhiteSpace(options.TracePath))
            {
                throw new McpException(
                    $"This server reads the single trace '{options.TracePath}'; a folder argument is not accepted.");
            }

            discovered = McpRunDiscovery.DiscoverFolder(folder);
        }
        else
        {
            discovered = McpRunDiscovery.Discover(options);
        }

        var effective = Math.Clamp(limit, 1, MaxRunLimit);
        var runs = discovered.Runs.Take(effective).Select(run =>
        {
            var failures = run.Archive.Tests.Where(test => !test.Succeeded).ToArray();
            return new
            {
                runId = run.Archive.RunId,
                traceFile = run.TraceFile,
                startedAtUtc = Timestamp(run.Archive.RunStartedAtUtc),
                completedAtUtc = Timestamp(run.Archive.RunCompletedAtUtc),
                outcomes = run.Archive.Tests
                    .GroupBy(test => test.Outcome, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                failingTests = failures.Take(MaxFailingTestsPerRun).Select(test => new
                {
                    testId = test.TestId,
                    name = test.Name
                }),
                failingTestsTruncated = failures.Length > MaxFailingTestsPerRun
            };
        });
        return Json(new
        {
            root = discovered.Root,
            runs,
            truncated = discovered.Runs.Count > effective,
            skipped = discovered.Skipped.Count == 0
                ? null
                : discovered.Skipped.Select(skip => new
                {
                    traceFile = skip.TraceFile,
                    reason = skip.Reason
                })
        });
    }

    /// <summary>Reads the failure entry of one non-succeeded test.</summary>
    [McpServerTool(
        Name = "get_failure",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Reads one failure: the test's outcome, the error, the source location, the failing operation, " +
        "the other operations that failed, the recorded shape mismatches and the test's evidence artifacts. " +
        "Defaults to the newest run's first non-succeeded test. Paths are relative to the server's folder. Read-only.")]
    public string GetFailure(
        [Description("Run id to read; defaults to the newest discovered run.")]
        string? runId = null,
        [Description("Test id (or exact test name) to read; defaults to the first non-succeeded test of the run.")]
        string? testId = null)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var run = McpRunDiscovery.ResolveRun(discovered, runId);
        var (test, note) = SelectTest(run, testId);
        var failure = test is null || test.Succeeded ? null : test.Failure;
        if (test is not null && test.Succeeded && note is null)
        {
            note = $"Test '{test.Name}' ({test.Outcome}) succeeded; there is no failure to read.";
        }

        IEnumerable<ProtoTraceOperation> failedOperations = test is null
            ? []
            : test.Operations.Where(operation =>
                operation.Failed && operation.Kind != "test.execution" && operation.SpanId != failure?.SpanId);
        var failedPage = failedOperations.Take(MaxFailedOperations + 1).ToArray();
        var (mismatches, mismatchesTruncated) = ReadMismatches(test, failure);

        return Json(new
        {
            runId = run.Archive.RunId,
            traceFile = run.TraceFile,
            note,
            test = test is null
                ? null
                : new
                {
                    testId = test.TestId,
                    name = test.Name,
                    outcome = test.Outcome,
                    durationMs = test.DurationMs
                },
            failure = failure is null ? null : OperationDetail(failure),
            failedOperations = failedPage.Take(MaxFailedOperations).Select(OperationDetail),
            failedOperationsTruncated = failedPage.Length > MaxFailedOperations,
            mismatches = mismatches.Count == 0 ? null : mismatches.Take(MaxMismatches),
            mismatchesTruncated,
            artifacts = test is null || test.Artifacts.Count == 0
                ? null
                : test.Artifacts.Take(MaxArtifactsPerTest).Select(artifact => new
                {
                    name = artifact.Name,
                    mediaType = artifact.MediaType,
                    description = artifact.Description,
                    archivePath = artifact.ArchivePath,
                    sizeBytes = artifact.SizeBytes,
                    error = artifact.Error
                }),
            artifactsTruncated = test is not null && test.Artifacts.Count > MaxArtifactsPerTest,
            skipped = discovered.Skipped.Count == 0
                ? null
                : discovered.Skipped.Select(skip => new
                {
                    traceFile = skip.TraceFile,
                    reason = skip.Reason
                })
        });
    }

    /// <summary>Reads the coverage totals and uncovered units from the run's embedded report.</summary>
    [McpServerTool(
        Name = "get_coverage",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Reads the coverage totals and the uncovered units from the JSON report a ProtoTest.Reporting " +
        "sink embedded in the run, with one suggestion per endpoint on the page: extend the test that already " +
        "calls it, or write a new test shaped like the named one. States a missing report " +
        "instead of inventing numbers. Read-only.")]
    public string GetCoverage(
        [Description("Run id to read; defaults to the newest discovered run.")]
        string? runId = null,
        [Description("Only units of this target, for example 'Northstar:Api'.")]
        string? target = null,
        [Description("Only units of this category, for example 'OpenAPI Property'.")]
        string? category = null,
        [Description("Set false to return the totals without the uncovered units; defaults to true.")]
        bool includeUncovered = true,
        [Description("Number of uncovered units to skip for paging; defaults to 0.")]
        int offset = 0,
        [Description("Maximum uncovered units to return, 1-200; defaults to 50.")]
        int limit = DefaultUncoveredLimit)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var run = McpRunDiscovery.ResolveRun(discovered, runId);
        ProtoTraceReport? report;
        string? reportReason;
        try
        {
            if (!ProtoTraceReport.TryRead(run.Archive, out report, out reportReason))
            {
                return Json(new
                {
                    runId = run.Archive.RunId,
                    traceFile = run.TraceFile,
                    note = reportReason,
                    reportArtifact = (object?)null,
                    totals = (object?)null,
                    uncovered = Array.Empty<object>(),
                    uncoveredTotal = 0,
                    truncated = false
                });
            }
        }
        catch (InvalidDataException exception)
        {
            throw new McpException(exception.Message);
        }

        // The suggestions walk the report in the same depth-first order, so they pair with the units by position.
        var suggestions = ProtoDiagnosis.SuggestCoverage(run.Archive);
        var units = report.Flatten()
            .Where(item => string.Equals(item.Kind, "coverage", StringComparison.OrdinalIgnoreCase) && item.IsCovered is false)
            .Select((item, index) => (Item: item, Suggestion: index < suggestions.Count ? suggestions[index] : null));
        if (!string.IsNullOrWhiteSpace(target))
        {
            units = units.Where(unit => string.Equals(unit.Item.TargetName, target, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            units = units.Where(unit => string.Equals(unit.Item.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        var uncovered = units.ToArray();
        var start = Math.Max(0, offset);
        var pageArray = includeUncovered
            ? uncovered.Skip(start).Take(Math.Clamp(limit, 1, MaxUncoveredLimit)).ToArray()
            : [];
        return Json(new
        {
            runId = run.Archive.RunId,
            traceFile = run.TraceFile,
            note = (string?)null,
            reportArtifact = new
            {
                name = report.Artifact.Name,
                archivePath = report.Artifact.ArchivePath,
                sizeBytes = report.Artifact.SizeBytes
            },
            totals = new
            {
                report.Summary.CoverageTotal,
                report.Summary.Covered,
                report.Summary.Uncovered,
                report.Summary.CoveragePercentage
            },
            uncovered = pageArray.Select(unit => new
            {
                target = unit.Item.TargetName,
                category = unit.Item.Category,
                identifier = unit.Item.Identifier,
                displayName = unit.Item.DisplayName,
                count = unit.Item.Count,
                message = unit.Item.Message,
                endpoint = unit.Suggestion?.Endpoint
            }),
            // One suggestion per endpoint on the page: its units share the test to start from.
            suggestions = pageArray
                .Where(unit => unit.Suggestion is not null)
                .Select(unit => unit.Suggestion!)
                .DistinctBy(suggestion => (suggestion.Target, suggestion.Endpoint ?? suggestion.Identifier))
                .Take(MaxSuggestions)
                .Select(suggestion => new
                {
                    target = suggestion.Target,
                    endpoint = suggestion.Endpoint ?? suggestion.Identifier,
                    action = suggestion.Action,
                    test = suggestion.Test,
                    sourceFile = suggestion.SourceFile,
                    reason = suggestion.Reason
                }),
            uncoveredTotal = uncovered.Length,
            truncated = includeUncovered && start + pageArray.Length < uncovered.Length
        });
    }

    /// <summary>Reads the deterministic diagnosis of one run, or one failing test's context package.</summary>
    [McpServerTool(
        Name = "get_diagnosis",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Reads the deterministic diagnosis of one run: the digest with the outcome counts, each " +
        "non-succeeded test's selected failure and its explanation, the run gates, the findings and " +
        "the coverage the report published. detail='context' returns one failing test's context package " +
        "with the ancestor chain, section previews, the source snippet, artifacts, state changes and " +
        "report rows. Read-only.")]
    public string GetDiagnosis(
        [Description("Run id to read; defaults to the newest discovered run.")]
        string? runId = null,
        [Description("Test id (or exact test name) to read; defaults to the first non-succeeded test of the run. Only used with detail='context'.")]
        string? testId = null,
        [Description("'summary' returns the run's diagnosis document; 'context' returns one failing test's context package. Defaults to 'summary'.")]
        string? detail = null)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var run = McpRunDiscovery.ResolveRun(discovered, runId);
        var kind = (detail ?? "summary").Trim().ToLowerInvariant();
        if (kind == "summary")
        {
            return McpOutput.Rewrite(ProtoDiagnosisJson.ToJson(ProtoDiagnosis.Read(run.Archive)), Root);
        }

        if (kind == "context")
        {
            var (test, note) = SelectTest(run, testId);
            if (test is null)
            {
                throw new McpException(note ?? $"No test to read a context for in run '{run.Archive.RunId}'.");
            }

            if (test.Succeeded)
            {
                throw new McpException(
                    $"Test '{test.Name}' ({test.Outcome}) succeeded; there is no diagnosis context to read.");
            }

            return McpOutput.Rewrite(ProtoDiagnosisJson.ToJson(ProtoDiagnosis.ReadContext(run.Archive, test.TestId)), Root);
        }

        throw new McpException($"Unknown detail '{detail}'; use 'summary' or 'context'.");
    }

    private static (ProtoTraceTest? Test, string? Note) SelectTest(ProtoTraceRun run, string? testId)
    {
        if (!string.IsNullOrWhiteSpace(testId))
        {
            var matches = run.Archive.Tests
                .Where(test => string.Equals(test.TestId, testId, StringComparison.Ordinal)
                    || string.Equals(test.Name, testId, StringComparison.Ordinal))
                .ToArray();
            return matches.Length switch
            {
                1 => (matches[0], null),
                > 1 => throw new McpException(
                    $"'{testId}' matches {matches.Length} tests in run '{run.Archive.RunId}'; use the test id."),
                _ => throw new McpException(
                    $"No test '{testId}' in run '{run.Archive.RunId}' ({run.Archive.Tests.Count} test(s)).")
            };
        }

        var failing = run.Archive.Tests.FirstOrDefault(test => !test.Succeeded);
        return failing is null
            ? (null, $"All {run.Archive.Tests.Count} test(s) in run '{run.Archive.RunId}' succeeded; there is no failure to read.")
            : (failing, null);
    }

    private static object OperationDetail(ProtoTraceOperation operation) => new
    {
        spanId = operation.SpanId,
        kind = operation.Kind,
        name = operation.Name,
        source = operation.Source,
        phase = operation.Phase,
        status = operation.Status,
        entityKind = operation.EntityKind,
        entityId = operation.EntityId,
        errorType = operation.ErrorType,
        errorMessage = Truncate(operation.ErrorMessage),
        sourceFile = operation.SourceFile,
        sourceLine = operation.SourceLine,
        sourceFunction = operation.SourceFunction,
        subject = operation.Subject
    };

    private static (List<MismatchDetail> Mismatches, bool Truncated) ReadMismatches(
        ProtoTraceTest? test,
        ProtoTraceOperation? failure)
    {
        if (test is null)
        {
            return ([], false);
        }

        // The failure operation first: a shape assertion records its mismatches there. Any other failed
        // operation can carry them too (a partial test), so the scan stays inside the test.
        var candidates = new List<ProtoTraceOperation>();
        if (failure is not null)
        {
            candidates.Add(failure);
        }

        candidates.AddRange(test.Operations.Where(operation => operation != failure));
        foreach (var operation in candidates)
        {
            var recorded = ProtoTraceOperation.ReadMismatches(operation);
            if (recorded.Count == 0)
            {
                continue;
            }

            return (
                [.. recorded.Select(mismatch => new MismatchDetail(
                    mismatch.Path,
                    mismatch.Reason,
                    mismatch.Expected,
                    mismatch.Actual))],
                recorded.Count > MaxMismatches);
        }

        return ([], false);
    }

    /// <summary>Bounds one recorded message so a hostile or enormous error cannot flood the tool output.</summary>
    private static string? Truncate(string? message)
        => message is null || message.Length <= MaxErrorMessageCharacters
            ? message
            : string.Concat(
                message.AsSpan(0, MaxErrorMessageCharacters),
                $"... (truncated; {message.Length} characters)");

    private static string? Timestamp(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private string Json(object value) => McpOutput.Write(value, Root);

    // The folder paths in the output are relative to: the project folder, or the folder of the one trace file.
    private string? Root => options.ProjectDirectory ?? Path.GetDirectoryName(options.TracePath);

    private sealed record MismatchDetail(
        string? PropertyPath,
        string? Reason,
        JsonElement? Expected,
        JsonElement? Actual);
}
