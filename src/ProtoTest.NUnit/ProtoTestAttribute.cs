namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using global::NUnit.Framework.Interfaces;
using ProtoTest.Core;

/// <summary>
/// NUnit test attribute that manages the <see cref="ProtoExecutionContext"/> lifecycle for each test method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ProtoTestAttribute : TestAttribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        var method = test.Method!.MethodInfo;
        var attributes = ProtoAttributeResolver.Resolve(method);
        var skipReason = ProtoTestSkip.GetReason(attributes, ProtoTestAssembly.Host);
        if (skipReason is not null)
        {
            // Skipping before the lifecycle starts keeps the trace honest: nothing ran, so nothing failed.
            Assert.Ignore(skipReason);
        }

        ProtoTestAssembly.Host
            .StartTestAsync(ProtoTestName.FromMethod(method), method, attributes, NUnitAttachmentPublisher.Instance)
            .GetAwaiter()
            .GetResult();
    }

    public void AfterTest(ITest test)
    {
        var nunitResult = TestContext.CurrentContext.Result;
        var result = nunitResult.Outcome.Status switch
        {
            TestStatus.Passed => ProtoTestResult.Passed,
            TestStatus.Failed => ProtoTestResult.Failed(new ProtoTraceError(
                $"NUnit.{nunitResult.Outcome.Label ?? TestStatus.Failed.ToString()}",
                string.IsNullOrWhiteSpace(nunitResult.Message)
                    ? "NUnit reported a failed test without a failure message."
                    : nunitResult.Message,
                nunitResult.StackTrace)),
            TestStatus.Skipped => ProtoTestResult.Skipped,
            TestStatus.Inconclusive => ProtoTestResult.Skipped,
            // NUnit's Warning means the test ran and passed with warnings attached; Partial is the
            // outcome that keeps the warning visible instead of reading it as an unknown state.
            TestStatus.Warning => new ProtoTestResult(ProtoTraceOutcome.Partial),
            _ => ProtoTestResult.Unknown
        };

        // Execute async post-test hooks, dispose the context scope, and clear ambient state.
        ProtoTestAssembly.Host
            .CompleteTestAsync(result)
            .GetAwaiter()
            .GetResult();
    }

}
