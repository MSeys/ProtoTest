namespace ProtoTest.MSTest;

using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

/// <summary>
/// Custom MSTest method attribute that manages the ProtoTest execution context lifecycle asynchronously.
/// MSTest invokes this once per data row, so each row is its own lifecycle and its own trace.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestAttribute([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1) : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    private const string FrameworkName = "MSTest";

    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        var methodInfo = testMethod.MethodInfo;
        var preparation = ProtoTestAdapter.Prepare(methodInfo, ProtoTestAssembly.Host, RowName(testMethod));
        if (!preparation.CanRun)
        {
            var skipReason = preparation.SkipReason;
            // MSTest.TestFramework 4.4 has no public dynamic-skip API: an ignored result is what the
            // runner reports as skipped. Its IgnoreReason is internal, so the reason travels on the
            // public LogOutput and is prefixed to the display name. Skipping before the lifecycle
            // starts keeps the trace honest.
            return
            [
                new TestResult
                {
                    Outcome = UnitTestOutcome.Ignored,
                    DisplayName = $"{methodInfo.Name} (skipped: {skipReason})",
                    LogOutput = skipReason
                }
            ];
        }

        var attachmentPublisher = new MSTestAttachmentPublisher();
        TestResult[]? results = null;
        var scope = await ProtoTestScope.StartAsync(preparation, ProtoTestAssembly.Host, attachmentPublisher);
        try
        {
            results = await base.ExecuteAsync(testMethod);
            scope.Result = ToProtoTestResult(results.Length == 0 ? null : results[0]);
            return results;
        }
        finally
        {
            await scope.DisposeAsync();

            // One invocation is one row, so this row's files belong on this row's result.
            if (results is { Length: > 0 } && attachmentPublisher.Files.Count > 0)
            {
                var primary = results[0];
                primary.ResultFiles = [.. primary.ResultFiles ?? [], .. attachmentPublisher.Files];
            }
        }
    }

    /// <summary>
    /// The row's trace name: the stable method name with the row's arguments appended, so the rows of
    /// one method are distinguishable in the trace.
    /// </summary>
    private static string? RowName(ITestMethod testMethod)
        => testMethod.Arguments is { Length: > 0 } arguments
            ? $"{ProtoTestName.FromMethod(testMethod.MethodInfo)}[{string.Join(", ", arguments.Select(argument => argument?.ToString() ?? "null"))}]"
            : null;

    /// <summary>
    /// Maps the one result MSTest produced for this invocation. MSTest calls the attribute once per
    /// data row, so there is no multi-row aggregation to do.
    /// </summary>
    internal static ProtoTestResult ToProtoTestResult(TestResult? result)
    {
        if (result is null) return ProtoTestResult.Unknown;
        return result.Outcome switch
        {
            UnitTestOutcome.Passed => ProtoTestResult.Passed,
            UnitTestOutcome.Ignored or UnitTestOutcome.Inconclusive => ProtoTestResult.Skipped,
            // MSTest reports a test it cannot run as skipped by default; match the runner.
            UnitTestOutcome.NotRunnable => ProtoTestResult.Skipped,
            UnitTestOutcome.Failed or UnitTestOutcome.Error when result.TestFailureException is OperationCanceledException =>
                ProtoTestResult.Cancelled(result.TestFailureException),
            UnitTestOutcome.Failed or UnitTestOutcome.Error when result.TestFailureException is not null =>
                ProtoTestResult.Failed(result.TestFailureException),
            UnitTestOutcome.Failed or UnitTestOutcome.Error => ProtoTestResult.Failed(
                FrameworkName, result.Outcome.ToString(), $"{FrameworkName} completed with outcome {result.Outcome}."),
            // A timeout or abort means the test never finished; recording it as cancelled keeps it
            // distinct from a failing assertion, matching how the other adapters report interruption.
            UnitTestOutcome.Timeout or UnitTestOutcome.Aborted => ProtoTestResult.Cancelled(
                FrameworkName, result.Outcome.ToString(), $"{FrameworkName} completed with outcome {result.Outcome}."),
            _ => ProtoTestResult.Unknown
        };
    }
}
