namespace ProtoTest.Mcp;

using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProtoTest.Traces;
using ProtoTest.Verification;

public sealed partial class ProtoTestMcpTools
{
    private const int MaxComparedTests = 50;

    /// <summary>Compares two runs test by test and names where each changed test's recordings part.</summary>
    [McpServerTool(
        Name = "compare_runs",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Compares two runs test by test: which tests broke, were fixed, still fail, are new or were " +
        "removed, and for each the first operation where the two recordings part (status or error type " +
        "changed, or an operation one run did not record). Defaults to the newest run against the run " +
        "before it. Use it after a rerun to see what a change did, or against a default-branch trace to " +
        "see where a failing run left the green one. Read-only.")]
    public string CompareRuns(
        [Description("Run id of the baseline; defaults to the run before the current one.")]
        string? baselineRunId = null,
        [Description("Run id of the run under review; defaults to the newest discovered run.")]
        string? currentRunId = null,
        [Description("A .prototrace file to use as the baseline instead of a discovered run, for example a trace downloaded from the default branch.")]
        string? baselineTrace = null)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var current = McpRunDiscovery.ResolveRun(discovered, currentRunId);
        var baseline = ResolveBaseline(discovered, current, baselineRunId, baselineTrace);
        var comparison = ProtoVerification.Compare(baseline.Archive, current.Archive);
        var changed = comparison.Tests.Where(test => test.Change != ProtoTestChanges.Unchanged).ToArray();

        return Json(new
        {
            baseline = new { runId = baseline.Archive.RunId, traceFile = baseline.TraceFile },
            current = new { runId = current.Archive.RunId, traceFile = current.TraceFile },
            counts = comparison.Counts,
            hasBroken = comparison.HasBroken,
            tests = changed.Take(MaxComparedTests).Select(test => new
            {
                name = test.Name,
                change = test.Change,
                baselineOutcome = test.BaselineOutcome,
                currentOutcome = test.CurrentOutcome,
                divergence = test.Divergence is not { } divergence
                    ? null
                    : new
                    {
                        reason = divergence.Reason,
                        baseline = divergence.Baseline is null ? null : Compared(divergence.Baseline),
                        current = divergence.Current is null ? null : Compared(divergence.Current)
                    }
            }),
            truncated = changed.Length > MaxComparedTests
        });
    }

    private static ProtoTraceRun ResolveBaseline(
        ProtoTraceFolderRuns discovered,
        ProtoTraceRun current,
        string? baselineRunId,
        string? baselineTrace)
    {
        if (!string.IsNullOrWhiteSpace(baselineTrace))
        {
            if (!string.IsNullOrWhiteSpace(baselineRunId))
            {
                throw new McpException("Pass baselineRunId or baselineTrace, not both.");
            }

            return ProtoTraceDiscovery.TryOpen(baselineTrace, out var run, out var reason)
                ? run
                : throw new McpException($"Could not read baseline trace '{baselineTrace}': {reason}.");
        }

        if (!string.IsNullOrWhiteSpace(baselineRunId))
        {
            return McpRunDiscovery.ResolveRun(discovered, baselineRunId);
        }

        // Runs are newest first, so the baseline is the first run older than the current one.
        var index = discovered.Runs.ToList().FindIndex(run => ReferenceEquals(run, current));
        return index >= 0 && index + 1 < discovered.Runs.Count
            ? discovered.Runs[index + 1]
            : throw new McpException(
                $"Run '{current.Archive.RunId}' has no older run to compare with under '{discovered.Root}'. " +
                "Pass baselineRunId or baselineTrace.");
    }

    private static object Compared(ProtoComparedOperation operation) => new
    {
        kind = operation.Kind,
        name = operation.Name,
        subject = operation.Subject,
        status = operation.Status,
        errorType = operation.ErrorType,
        errorMessage = Truncate(operation.ErrorMessage),
        durationMs = operation.DurationMs,
        sourceFile = operation.SourceFile,
        sourceLine = operation.SourceLine
    };
}
