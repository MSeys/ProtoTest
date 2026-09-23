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
    private const string FrameworkName = "NUnit";

    private ProtoTestScope? _scope;

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        var preparation = ProtoTestAdapter.Prepare(test.Method!.MethodInfo, ProtoTestAssembly.Host);
        if (!preparation.CanRun)
        {
            Assert.Ignore(preparation.SkipReason!);
        }

        _scope = ProtoTestAsync.RunSync(() => new ValueTask<ProtoTestScope>(ProtoTestScope.StartAsync(
            preparation, ProtoTestAssembly.Host, NUnitAttachmentPublisher.Instance)));
    }

    public void AfterTest(ITest test)
    {
        if (_scope is null)
        {
            return;
        }

        _scope.Result = MapResult();
        ProtoTestAsync.RunSync(() => _scope.DisposeAsync());
        _scope = null;
    }

    private static ProtoTestResult MapResult()
    {
        var nunitResult = TestContext.CurrentContext.Result;
        return nunitResult.Outcome.Status switch
        {
            TestStatus.Passed => ProtoTestResult.Passed,
            TestStatus.Failed => ProtoTestResult.Failed(
                FrameworkName,
                nunitResult.Outcome.Label ?? TestStatus.Failed.ToString(),
                string.IsNullOrWhiteSpace(nunitResult.Message)
                    ? "NUnit reported a failed test without a failure message."
                    : nunitResult.Message,
                nunitResult.StackTrace),
            TestStatus.Skipped => ProtoTestResult.Skipped,
            TestStatus.Inconclusive => ProtoTestResult.Skipped,
            // NUnit's Warning means the test ran and passed with warnings attached; Partial is the
            // outcome that keeps the warning visible instead of reading it as an unknown state.
            TestStatus.Warning => ProtoTestResult.Partial,
            _ => ProtoTestResult.Unknown
        };
    }
}
