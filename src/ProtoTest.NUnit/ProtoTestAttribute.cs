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
        var attributes = ProtoAttributeResolver.Resolve(test.Method!.MethodInfo);
        ProtoTestAssembly.Host
            .StartTestAsync(test.FullName, test.Method!.MethodInfo, attributes, NUnitAttachmentPublisher.Instance)
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
            _ => ProtoTestResult.Unknown
        };

        // Execute async post-test hooks, dispose the context scope, and clear ambient state.
        ProtoTestAssembly.Host
            .CompleteTestAsync(result)
            .GetAwaiter()
            .GetResult();
    }

}
