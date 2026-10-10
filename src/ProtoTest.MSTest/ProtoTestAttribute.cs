namespace ProtoTest.MSTest;

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
        Exception? bodyEscape = null;
        var scope = await ProtoTestScope.StartAsync(preparation, ProtoTestAssembly.Host, attachmentPublisher);
        try
        {
            results = await base.ExecuteAsync(testMethod);
            scope.Result = ToProtoTestResult(results.Length == 0 ? null : results[0]);
        }
        catch (Exception exception)
        {
            bodyEscape = exception;
            scope.Result = ProtoTestResult.FromException(exception);
        }

        ProtoCleanupException? cleanup = null;
        try
        {
            await scope.DisposeAsync();
        }
        catch (ProtoCleanupException exception)
        {
            cleanup = exception;
        }

        if (cleanup is not null)
        {
            // The TestFailureException setter aggregates a second assignment with the exception already
            // stored, so writing the cleanup onto a failed result reports the assertion and the cleanup
            // as two errors. A new result carries only the cleanup exception. Its message already leads
            // with the body failure, and its inner exception is that failure.
            return [ReportCleanup(results is { Length: > 0 } ? results[0] : null, cleanup, attachmentPublisher)];
        }

        if (results is { Length: > 0 })
        {
            // One invocation is one row, so this row's files belong on this row's result.
            if (attachmentPublisher.Files.Count > 0)
            {
                var primary = results[0];
                primary.ResultFiles = [.. primary.ResultFiles ?? [], .. attachmentPublisher.Files];
            }

            return results;
        }

        if (bodyEscape is not null)
        {
            ExceptionDispatchInfo.Capture(bodyEscape).Throw();
        }

        return results ?? [];
    }

    /// <summary>
    /// A result whose only failure is the cleanup exception. A body that already failed keeps its
    /// outcome; the cleanup exception's message leads with that failure.
    /// </summary>
    private static TestResult ReportCleanup(
        TestResult? source,
        ProtoCleanupException cleanup,
        MSTestAttachmentPublisher attachmentPublisher)
    {
        var reported = new TestResult
        {
            Outcome = source?.Outcome is UnitTestOutcome.Failed or UnitTestOutcome.Error
                ? source.Outcome
                : UnitTestOutcome.Failed,
            DisplayName = source?.DisplayName,
            LogOutput = source?.LogOutput,
            LogError = source?.LogError,
            DebugTrace = source?.DebugTrace,
            TestContextMessages = source?.TestContextMessages,
            Duration = source?.Duration ?? TimeSpan.Zero,
            TestFailureException = cleanup
        };
        if (source?.ResultFiles is { Count: > 0 } || attachmentPublisher.Files.Count > 0)
        {
            reported.ResultFiles = [.. source?.ResultFiles ?? [], .. attachmentPublisher.Files];
        }

        return reported;
    }

    /// <summary>
    /// The row's trace name: the stable method name with the row's arguments appended, so the rows of
    /// one method are distinguishable in the trace.
    /// </summary>
    private static string? RowName(ITestMethod testMethod)
        => testMethod.Arguments is { Length: > 0 } arguments
            ? ProtoTestName.ForRow(testMethod.MethodInfo, arguments)
            : null;

    /// <summary>
    /// Maps the one result MSTest produced for this invocation. MSTest calls the attribute once per
    /// data row, so there is no multi-row aggregation to do. A failure with an exception goes through
    /// the shared classifier, so a cancelled body maps the same way here as in every other adapter.
    /// </summary>
    // MSTest reports what the test method threw inside its own TestFailedException; the method's exception is the
    // inner one. Classifying the wrapper would call a cancelled body failed.
    internal static Exception Unwrap(Exception exception)
        => exception.GetType().Name == "TestFailedException" && exception.InnerException is { } thrown ? thrown : exception;

    internal static ProtoTestResult ToProtoTestResult(TestResult? result)
    {
        if (result is null) return ProtoTestResult.Unknown;
        return result.Outcome switch
        {
            UnitTestOutcome.Passed => ProtoTestResult.Passed,
            UnitTestOutcome.Ignored or UnitTestOutcome.Inconclusive => ProtoTestResult.Skipped,
            // MSTest reports a test it cannot run as skipped by default; match the runner.
            UnitTestOutcome.NotRunnable => ProtoTestResult.Skipped,
            UnitTestOutcome.Failed or UnitTestOutcome.Error when result.TestFailureException is not null =>
                ProtoTestResult.FromException(Unwrap(result.TestFailureException)),
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
