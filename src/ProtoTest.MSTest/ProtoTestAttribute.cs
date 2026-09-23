namespace ProtoTest.MSTest;

using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

/// <summary>
/// Custom MSTest method attribute that manages the ProtoTest execution context lifecycle asynchronously.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestAttribute([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1) : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    private const string FrameworkName = "MSTest";

    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        var methodInfo = testMethod.MethodInfo;
        var preparation = ProtoTestAdapter.Prepare(methodInfo, ProtoTestAssembly.Host);
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
            scope.Result = ToProtoTestResult(results);
            return results;
        }
        finally
        {
            await scope.DisposeAsync();

            // The lifecycle spans every data row, so attach the collected files to the first
            // result rather than duplicating them onto each row.
            if (results is { Length: > 0 } && attachmentPublisher.Files.Count > 0)
            {
                var primary = results[0];
                primary.ResultFiles = [.. primary.ResultFiles ?? [], .. attachmentPublisher.Files];
            }
        }
    }

    internal static ProtoTestResult ToProtoTestResult(IReadOnlyList<TestResult>? results)
    {
        if (results is null || results.Count == 0) return ProtoTestResult.Unknown;
        if (results.All(result => result.Outcome == UnitTestOutcome.Passed)) return ProtoTestResult.Passed;
        if (results.All(result => result.Outcome is UnitTestOutcome.Ignored or UnitTestOutcome.Inconclusive))
            return ProtoTestResult.Skipped;
        if (results.All(result => result.Outcome is UnitTestOutcome.Passed or UnitTestOutcome.Ignored or UnitTestOutcome.Inconclusive))
        {
            // Some data rows ran while others were skipped: Partial keeps both facts visible, where
            // Passed would hide the skip and Skipped would hide the rows that ran.
            return ProtoTestResult.Partial;
        }

        var failed = results.FirstOrDefault(result => result.Outcome is
            UnitTestOutcome.Failed or UnitTestOutcome.Error);
        if (failed is not null)
        {
            if (failed.TestFailureException is not null)
                return ProtoTestResult.Failed(failed.TestFailureException);
            return ProtoTestResult.Failed(
                FrameworkName, failed.Outcome.ToString(), $"{FrameworkName} completed with outcome {failed.Outcome}.");
        }

        var interrupted = results.FirstOrDefault(result => result.Outcome is
            UnitTestOutcome.Timeout or UnitTestOutcome.Aborted);
        if (interrupted is not null)
        {
            // A timeout or abort means the test never finished; recording it as cancelled keeps it
            // distinct from a failing assertion, matching how the other adapters report interruption.
            return ProtoTestResult.Cancelled(
                FrameworkName, interrupted.Outcome.ToString(), $"{FrameworkName} completed with outcome {interrupted.Outcome}.");
        }

        return ProtoTestResult.Unknown;
    }
}
