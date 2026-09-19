namespace ProtoTest.Xunit3;

using System.Reflection;
using ProtoTest.Core;
using Xunit.v3;

/// <summary>
/// Internal utility class providing shared context lifecycle management for xUnit v3 test attributes.
/// </summary>
internal static class ProtoTestLifecycleHandler
{
    /// <summary>
    /// Starts the test context and executes before-test hooks.
    /// </summary>
    internal static void Before(MethodInfo methodUnderTest)
    {
        var attributes = ProtoAttributeResolver.Resolve(methodUnderTest);
        var skipReason = ProtoTestSkip.GetReason(attributes, ProtoTestAssembly.Host);
        if (skipReason is not null)
        {
            // Skipping before the lifecycle starts keeps the trace honest: nothing ran, so nothing failed.
            global::Xunit.Assert.Skip(skipReason);
        }

        ProtoTestAssembly.Host
            .StartTestAsync(ProtoTestName.FromMethod(methodUnderTest), methodUnderTest, attributes, Xunit3AttachmentPublisher.Instance)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Executes after-test hooks and cleans up the active <see cref="ProtoExecutionContext"/> using the
    /// state xUnit recorded for the test that just finished.
    /// </summary>
    internal static void After(MethodInfo methodUnderTest)
        => Complete(methodUnderTest, global::Xunit.TestContext.Current.TestState);

    /// <summary>
    /// Completes the active lifecycle with the outcome mapped from an xUnit test result state. xUnit owns
    /// the ambient state, so this overload exists so the mapping can be exercised directly.
    /// </summary>
    internal static void Complete(MethodInfo methodUnderTest, global::Xunit.TestResultState? state)
    {
        var result = MapResult(state);
        ProtoTestAssembly.Host
            .CompleteTestAsync(result)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Maps an xUnit test result state onto the outcome ProtoTest records.</summary>
    internal static ProtoTestResult MapResult(global::Xunit.TestResultState? state)
        => state?.Result switch
        {
            global::Xunit.TestResult.Passed => ProtoTestResult.Passed,
            global::Xunit.TestResult.Skipped or global::Xunit.TestResult.NotRun => ProtoTestResult.Skipped,
            global::Xunit.TestResult.Failed => ProtoTestResult.Failed(new ProtoTraceError(
                state.ExceptionTypes?.FirstOrDefault() ?? "xUnit.TestFailure",
                state.ExceptionMessages?.FirstOrDefault() ?? "The xUnit test failed.",
                state.ExceptionStackTraces?.FirstOrDefault())),
            _ => ProtoTestResult.Unknown
        };
}
