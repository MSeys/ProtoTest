namespace ProtoTest.Mcp;

using System.ComponentModel;
using ModelContextProtocol.Server;
using ProtoTest.Diagnosis;

public sealed partial class ProtoTestMcpTools
{
    private const int MaxReviewedTests = 30;
    private const int MaxReviewFindingsPerTest = 10;

    /// <summary>Reviews what each test of a run proves, with the next step for every finding.</summary>
    [McpServerTool(
        Name = "review_tests",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Reviews what each test proves from what it recorded: a test body with no check, a call no check " +
        "looked at, and time in the body with no recorded operation (a sleep or an untraced call). Every " +
        "finding carries the next step and the source location when the trace captured one. Tests with " +
        "the most findings come first; clean tests are counted. Use it to improve tests, and after writing " +
        "a test to confirm it checks what it calls. Read-only.")]
    public string ReviewTests(
        [Description("Run id to read; defaults to the newest discovered run.")]
        string? runId = null,
        [Description("The tests to review, by exact name or id; defaults to every test of the run.")]
        string[]? tests = null)
    {
        var discovered = McpRunDiscovery.Discover(options);
        var run = McpRunDiscovery.ResolveRun(discovered, runId);
        var review = ProtoDiagnosis.Review(run.Archive, tests);
        var flagged = review.Tests.Where(test => !test.Clean).ToArray();

        return Json(new
        {
            runId = run.Archive.RunId,
            traceFile = run.TraceFile,
            reviewed = review.Tests.Count,
            clean = review.Tests.Count - flagged.Length,
            counts = review.Counts,
            tests = flagged.Take(MaxReviewedTests).Select(test => new
            {
                testId = test.TestId,
                name = test.Name,
                outcome = test.Outcome,
                checks = test.Checks,
                calls = test.Calls,
                findings = test.Findings.Take(MaxReviewFindingsPerTest).Select(finding => new
                {
                    rule = finding.Rule,
                    message = finding.Message,
                    next = finding.Next,
                    subject = finding.Subject,
                    sourceFile = finding.SourceFile,
                    sourceLine = finding.SourceLine
                }),
                findingsTruncated = test.Findings.Count > MaxReviewFindingsPerTest
            }),
            truncated = flagged.Length > MaxReviewedTests
        });
    }
}
