namespace ProtoTest.RunnerContract.Tests;

/// <summary>
/// The cleanup boundary through each real runner: a body that passes, fails, skips at run time or is
/// cancelled, with cleanup that succeeds or fails under the Fail and Report policies. Each scenario is a
/// fresh process; the runner's exit code, its one reported test and the trace must all agree.
/// </summary>
[TestFixture]
public sealed class CleanupContractTests
{
    private const string CleanupFailed = "The test body passed, but cleanup failed: InvalidOperationException: CONTRACT-CLEANUP.";
    private const string SkippedCleanupFailed = "The test body was skipped, but cleanup failed: InvalidOperationException: CONTRACT-CLEANUP.";

    private static readonly Scenario[] Scenarios =
    [
        new(),
        new(CleanupFails: true),
        new(CleanupFails: true, Mode: "Report"),
        new(Body: "fail"),
        new(Body: "fail", CleanupFails: true),
        new(Body: "fail", CleanupFails: true, Mode: "Report"),
        new(Body: "skip"),
        new(Body: "skip", CleanupFails: true),
        new(Body: "skip", CleanupFails: true, Mode: "Report"),
        new(Body: "cancel")
    ];

    /// <summary>Every runner with every scenario it supports: xUnit v2 has no runtime skip.</summary>
    public static IEnumerable<TestCaseData> Cases()
        => from runner in Enum.GetValues<Runner>()
           from scenario in Scenarios
           where !(runner == Runner.Xunit && scenario.Body == "skip")
           select new TestCaseData(runner, scenario).SetArgDisplayNames(runner.ToString(), scenario.ToString());

    [TestCaseSource(nameof(Cases))]
    public async Task Scenario_ShouldReportOneTestThatAgreesWithItsTrace(Runner runner, Scenario scenario)
    {
        // Act
        var run = await FixtureSuites.RunAsync(runner, "CleanupContract", scenario);

        // Assert
        var failed = scenario.Body is "fail" or "cancel" || (scenario.CleanupFails && scenario.Mode == "Fail");
        var outcome = failed ? "Failed" : scenario.Body == "skip" ? "Skipped" : "Passed";
        var trace = ExpectedTraceOutcome(runner, scenario);
        var describe = Describe(run);
        Assert.That(run.Results, Has.Count.EqualTo(1), describe);
        Assert.That(run.Trace, Is.Not.Null, describe);
        Assert.That(run.Trace!.Tests, Has.Count.EqualTo(1), describe);
        var result = run.Results[0];
        var traced = run.Trace.Tests[0];
        var findings = Findings(traced);
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(failed ? FailedExitCode(runner) : 0), describe);
            Assert.That(result.Outcome, Is.EqualTo(outcome), describe);
            Assert.That(traced.Outcome, Is.EqualTo(trace), describe);
            Assert.That(run.Markers, Is.EqualTo(new[] { "body", "bad-release", "good-release" }), describe);
        });

        Assert.Multiple(() =>
        {
            switch (scenario)
            {
                case { Body: "pass", CleanupFails: true, Mode: "Fail" }:
                    Assert.That(result.Message, Does.Contain(CleanupFailed), describe);
                    break;
                case { Body: "fail", CleanupFails: true, Mode: "Fail" }:
                    // The body's failure stays first, the cleanup is attached after it, and the body is named once.
                    Assert.That(result.Message, Does.Contain("CONTRACT-BODY").And.Contain("CONTRACT-CLEANUP"), describe);
                    Assert.That(result.Message.IndexOf("CONTRACT-BODY", StringComparison.Ordinal), Is.LessThan(result.Message.IndexOf("CONTRACT-CLEANUP", StringComparison.Ordinal)), describe);
                    Assert.That(Occurrences(result.Message, "CONTRACT-BODY"), Is.EqualTo(1), describe);
                    break;
                case { Body: "fail" }:
                    Assert.That(result.Message, Does.Contain("CONTRACT-BODY").And.Not.Contain("CONTRACT-CLEANUP"), describe);
                    Assert.That(Occurrences(result.Message, "CONTRACT-BODY"), Is.EqualTo(1), describe);
                    break;
                case { Body: "skip", CleanupFails: true, Mode: "Fail" }:
                    Assert.That(result.Message, Does.Contain(SkippedCleanupFailed), describe);
                    break;
                case { Body: "skip" }:
                    Assert.That(result.Message, Does.Contain("CONTRACT-SKIP").And.Not.Contain("CONTRACT-CLEANUP"), describe);
                    break;
                case { Body: "cancel" }:
                    Assert.That(result.Message, Does.Contain(nameof(OperationCanceledException)), describe);
                    break;
                default:
                    Assert.That(result.Message, Does.Not.Contain("CONTRACT-"), describe);
                    break;
            }

            // A failed release is a finding on the test whatever the policy; with none, there is no finding.
            Assert.That(findings.Any(finding => finding.Contains("CONTRACT-CLEANUP", StringComparison.Ordinal)), Is.EqualTo(scenario.CleanupFails), describe);
        });
    }

    /// <summary>The outcome ProtoTest records for a scenario, in the archive's vocabulary.</summary>
    private static string ExpectedTraceOutcome(Runner runner, Scenario scenario) => scenario switch
    {
        { Body: "fail" } => "failed",
        // NUnit reports a cancelled test as a plain failure and exposes no exception type to classify.
        // MSTest hands the adapter its own wrapper around the thrown exception, so the shared classifier
        // never sees the cancellation: an open adapter gap this row pins until the adapter unwraps it.
        { Body: "cancel" } => runner is Runner.NUnit or Runner.MSTest ? "failed" : "cancelled",
        { CleanupFails: true, Mode: "Fail" } => "failed",
        { Body: "skip" } => "skipped",
        { CleanupFails: true } => "partial",
        _ => "succeeded"
    };

    internal static int FailedExitCode(Runner runner) => runner == Runner.TUnit ? 2 : 1;

    internal static IReadOnlyList<string> Findings(ProtoTest.Traces.ProtoTraceTest test)
        => [.. test.Evidence.Concat(test.Operations.SelectMany(operation => operation.Evidence))
            .Where(evidence => evidence.Record == "finding")
            .Select(evidence => $"{evidence.Name} {evidence.Data}")];

    internal static string Describe(ScenarioRun run)
    {
        var tests = run.Trace?.Tests.Select(test =>
            $"  {test.Name}: {test.Outcome}; failure: {test.Failure?.ErrorType}: {test.Failure?.ErrorMessage}; findings: {string.Join(" | ", Findings(test))}") ?? ["  (no trace archive)"];
        return string.Join(
            Environment.NewLine,
            [
                $"exit code {run.ExitCode}; markers: {string.Join(",", run.Markers)}",
                "results:",
                .. run.Results.Select(result => $"  {result.Name}: {result.Outcome}: {result.Message}"),
                "trace:",
                .. tests,
                "output:",
                run.Output
            ]);
    }

    /// <summary>
    /// How often a runner's message names a value. The lines xUnit prints for an inner exception (its
    /// <c>----</c> chain) repeat what the message above them already says, so they are not counted.
    /// </summary>
    private static int Occurrences(string message, string value)
        => message.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("----", StringComparison.Ordinal))
            .Sum(line => (line.Length - line.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length);
}
