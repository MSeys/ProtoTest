namespace ProtoTest.Xunit3;

using System.Reflection;
using ProtoTest.Core;
using Xunit.v3;

/// <summary>
/// The before/after implementation the xUnit v3 attributes share: one lifecycle, one skip path and one
/// result mapping, with the scope each attribute keeps for the test it wraps.
/// </summary>
internal interface IProtoTestXunit3Attribute : IBeforeAfterTestAttribute
{
    /// <summary>The lifecycle scope started before the test; completed after it.</summary>
    ProtoTestScope? Scope { get; set; }

    void IBeforeAfterTestAttribute.Before(MethodInfo methodUnderTest, IXunitTest test)
        => Scope = ProtoTestLifecycleHandler.Before(methodUnderTest);

    void IBeforeAfterTestAttribute.After(MethodInfo methodUnderTest, IXunitTest test)
    {
        ProtoTestLifecycleHandler.After(Scope);
        Scope = null;
    }
}

/// <summary>
/// Shared context lifecycle management for the xUnit v3 test attributes.
/// </summary>
internal static class ProtoTestLifecycleHandler
{
    private const string DefaultErrorType = "xUnit.TestFailure";

    /// <summary>Starts the test context and executes before-test hooks.</summary>
    internal static ProtoTestScope Before(MethodInfo methodUnderTest)
    {
        var preparation = ProtoTestAdapter.Prepare(methodUnderTest, ProtoTestAssembly.Host);
        if (!preparation.CanRun)
        {
            global::Xunit.Assert.Skip(preparation.SkipReason!);
        }

        return ProtoTestAsync.RunSync(() => new ValueTask<ProtoTestScope>(ProtoTestScope.StartAsync(
            preparation, ProtoTestAssembly.Host, Xunit3AttachmentPublisher.Instance)));
    }

    /// <summary>Completes the active lifecycle with the state xUnit recorded for the test that just finished.</summary>
    internal static void After(ProtoTestScope? scope)
        => Complete(scope, global::Xunit.TestContext.Current.TestState);

    /// <summary>
    /// Completes the lifecycle with an explicit state. xUnit owns the ambient state during a real run,
    /// so this overload exists so the mapping can be exercised directly.
    /// </summary>
    internal static void Complete(ProtoTestScope? scope, global::Xunit.TestResultState? state)
    {
        if (scope is null)
        {
            return;
        }

        scope.Result = MapResult(state);
        ProtoTestAsync.RunSync(() => scope.DisposeAsync());
    }

    /// <summary>Maps an xUnit test result state onto the outcome ProtoTest records.</summary>
    internal static ProtoTestResult MapResult(global::Xunit.TestResultState? state)
        => state?.Result switch
        {
            global::Xunit.TestResult.Passed => ProtoTestResult.Passed,
            global::Xunit.TestResult.Skipped or global::Xunit.TestResult.NotRun => ProtoTestResult.Skipped,
            global::Xunit.TestResult.Failed => ProtoTestResult.Failed(new ProtoTraceError(
                state.ExceptionTypes?.FirstOrDefault() ?? DefaultErrorType,
                state.ExceptionMessages?.FirstOrDefault() ?? "The xUnit test failed.",
                state.ExceptionStackTraces?.FirstOrDefault())),
            _ => ProtoTestResult.Unknown
        };
}
