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
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        var methodInfo = testMethod.MethodInfo;
        var attributes = ProtoAttributeResolver.Resolve(methodInfo);
        var attachmentPublisher = new MSTestAttachmentPublisher();
        TestResult[]? results = null;
        var lifecycleStarted = false;
        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(
                ProtoTestName.FromMethod(methodInfo), methodInfo, attributes, attachmentPublisher);
            lifecycleStarted = true;

            results = await base.ExecuteAsync(testMethod);
            return results;
        }
        finally
        {
            try
            {
                if (lifecycleStarted)
                {
                    await ProtoTestAssembly.Host.CompleteTestAsync(ToProtoTestResult(results));
                }
            }
            finally
            {
                // The lifecycle spans every data row, so attach the collected files to the first
                // result rather than duplicating them onto each row.
                if (results is { Length: > 0 } && attachmentPublisher.Files.Count > 0)
                {
                    var primary = results[0];
                    primary.ResultFiles = [.. primary.ResultFiles ?? [], .. attachmentPublisher.Files];
                }
            }
        }
    }

    private static ProtoTestResult ToProtoTestResult(IReadOnlyList<TestResult>? results)
    {
        if (results is null || results.Count == 0) return ProtoTestResult.Unknown;
        if (results.All(result => result.Outcome == UnitTestOutcome.Passed)) return ProtoTestResult.Passed;
        if (results.All(result => result.Outcome == UnitTestOutcome.Ignored)) return ProtoTestResult.Skipped;

        var failed = results.FirstOrDefault(result => result.Outcome is
            UnitTestOutcome.Failed or UnitTestOutcome.Error or UnitTestOutcome.Timeout or UnitTestOutcome.Aborted);
        if (failed is not null)
        {
            if (failed.TestFailureException is not null)
                return ProtoTestResult.Failed(failed.TestFailureException);
            return ProtoTestResult.Failed(new ProtoTraceError(
                $"MSTest.{failed.Outcome}",
                $"MSTest completed with outcome {failed.Outcome}."));
        }

        return ProtoTestResult.Unknown;
    }
}
