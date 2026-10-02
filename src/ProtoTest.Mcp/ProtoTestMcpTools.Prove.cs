namespace ProtoTest.Mcp;

using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProtoTest.Traces;
using ProtoTest.Verification;

public sealed partial class ProtoTestMcpTools
{
    private const int MaxProvedTests = 20;
    private const int MaxBrokenTests = 20;

    /// <summary>Proves a fix from recorded runs and returns the receipt.</summary>
    [McpServerTool(
        Name = "check_fix",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Proves a fix from recorded runs and returns the receipt. A test is proven when the baseline run " +
        "recorded it failing and every current run recorded it succeeded; the fix is proven when every " +
        "claimed test is, no test that passed in the baseline fails now, and the embedded reports verify " +
        "without a failing finding. Each unmet condition is a named reason. Call it after rerunning the " +
        "suite; a fix is done only when this says proven. Defaults to the newest run against the run " +
        "before it, claiming every test the baseline did not pass. Read-only.")]
    public string CheckFix(
        [Description("The tests the fix claims, by exact name; defaults to every test the baseline did not pass.")]
        string[]? tests = null,
        [Description("Run id of the run that shows the failure; defaults to the run before the oldest current run.")]
        string? baselineRunId = null,
        [Description("Run ids of the runs after the fix; defaults to the newest run. Pass two or more to guard against a test that passes once by chance.")]
        string[]? currentRunIds = null,
        [Description("A .prototrace file to use as the baseline instead of a discovered run.")]
        string? baselineTrace = null)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var currentRuns = currentRunIds is { Length: > 0 }
            ? currentRunIds.Select(id => McpRunDiscovery.ResolveRun(discovered, id)).ToArray()
            : [McpRunDiscovery.ResolveRun(discovered, null)];

        // Runs are newest first, so the oldest current run decides which run comes before.
        var oldest = currentRuns.OrderByDescending(run => discovered.Runs.ToList().IndexOf(run)).First();
        var baseline = ResolveBaseline(discovered, oldest, baselineRunId, baselineTrace);
        if (currentRuns.Any(run => ReferenceEquals(run, baseline)))
        {
            throw new McpException($"Run '{baseline.Archive.RunId}' cannot be both the baseline and a current run.");
        }

        var receipt = ProtoVerification.Prove(baseline.Archive, [.. currentRuns.Select(run => run.Archive)], tests);
        return Json(new
        {
            proven = receipt.Proven,
            baseline = new { runId = baseline.Archive.RunId, traceFile = baseline.TraceFile },
            current = currentRuns.Select(run => new { runId = run.Archive.RunId, traceFile = run.TraceFile }),
            tests = receipt.Tests.Take(MaxProvedTests).Select(test => new
            {
                name = test.Name,
                proven = test.Proven,
                reasons = test.Reasons.Count == 0
                    ? null
                    : test.Reasons.Select(reason => new { code = reason.Code, message = reason.Message }),
                changedAt = test.Divergence is not { } divergence
                    ? null
                    : new
                    {
                        reason = divergence.Reason,
                        baseline = divergence.Baseline is null ? null : Compared(divergence.Baseline),
                        current = divergence.Current is null ? null : Compared(divergence.Current)
                    }
            }),
            testsTruncated = receipt.Tests.Count > MaxProvedTests,
            brokenTests = receipt.BrokenTests.Take(MaxBrokenTests),
            brokenTestsTruncated = receipt.BrokenTests.Count > MaxBrokenTests,
            reportFindings = receipt.ReportVerdict?.Findings
                .Where(finding => finding.Severity == ProtoVerificationSeverities.Fail)
                .Take(MaxBrokenTests)
                .Select(finding => new { @class = finding.Class, message = finding.Message }),
            reportNote = receipt.ReportNote
        });
    }
}
