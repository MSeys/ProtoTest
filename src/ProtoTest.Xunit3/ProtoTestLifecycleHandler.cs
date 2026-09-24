namespace ProtoTest.Xunit3;

using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoTest.Core;
using Xunit.v3;

/// <summary>
/// The before/after implementation the xUnit v3 attributes share: one lifecycle, one skip path and one
/// result mapping. The scope is keyed by the test, not held by the attribute instance: xUnit may reuse
/// one attribute instance for several tests, and parallel rows must not clobber each other.
/// </summary>
internal interface IProtoTestXunit3Attribute : IBeforeAfterTestAttribute
{
    void IBeforeAfterTestAttribute.Before(MethodInfo methodUnderTest, IXunitTest test)
        => ProtoTestLifecycleHandler.Before(methodUnderTest, test);

    void IBeforeAfterTestAttribute.After(MethodInfo methodUnderTest, IXunitTest test)
        => ProtoTestLifecycleHandler.After(test);
}

/// <summary>
/// Shared context lifecycle management for the xUnit v3 test attributes.
/// </summary>
internal static class ProtoTestLifecycleHandler
{
    private const string DefaultErrorType = "xUnit.TestFailure";
    private static readonly ConditionalWeakTable<IXunitTest, ScopeHolder> Scopes = new();

    private sealed class ScopeHolder
    {
        public ProtoTestScope? Scope { get; set; }
    }

    /// <summary>Starts the test context and executes before-test hooks.</summary>
    internal static ProtoTestScope Before(MethodInfo methodUnderTest, IXunitTest? test)
    {
        // The display name distinguishes theory rows; the method-only form is the probe path.
        var preparation = ProtoTestAdapter.Prepare(methodUnderTest, ProtoTestAssembly.Host, test?.TestDisplayName);
        if (!preparation.CanRun)
        {
            global::Xunit.Assert.Skip(preparation.SkipReason!);
        }

        var scope = ProtoTestAsync.RunSync(() => new ValueTask<ProtoTestScope>(ProtoTestScope.StartAsync(
            preparation, ProtoTestAssembly.Host, Xunit3AttachmentPublisher.Instance)));
        if (test is not null)
        {
            Scopes.AddOrUpdate(test, new ScopeHolder { Scope = scope });
        }

        return scope;
    }

    /// <summary>Completes the test's lifecycle with the state xUnit recorded for it.</summary>
    internal static void After(IXunitTest? test)
    {
        if (test is null || !Scopes.TryGetValue(test, out var holder))
        {
            return;
        }

        var scope = holder.Scope;
        holder.Scope = null;
        global::Xunit.TestResultState? state = null;
        try
        {
            state = global::Xunit.TestContext.Current.TestState;
        }
        catch (Exception)
        {
            // A state read failure must not leave the lifecycle open: it completes as Unknown, which the
            // run-level contract verification reports loudly.
        }

        Complete(scope, state);
    }

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
            global::Xunit.TestResult.Failed when IsCancellation(state) => ProtoTestResult.Cancelled(
                state.ExceptionTypes?.FirstOrDefault() ?? DefaultErrorType,
                "Cancelled",
                state.ExceptionMessages?.FirstOrDefault() ?? "The xUnit test was cancelled.",
                state.ExceptionStackTraces?.FirstOrDefault()),
            global::Xunit.TestResult.Failed => ProtoTestResult.Failed(new ProtoTraceError(
                state.ExceptionTypes?.FirstOrDefault() ?? DefaultErrorType,
                state.ExceptionMessages?.FirstOrDefault() ?? "The xUnit test failed.",
                state.ExceptionStackTraces?.FirstOrDefault())),
            _ => ProtoTestResult.Unknown
        };

    private static bool IsCancellation(global::Xunit.TestResultState state)
        => state.ExceptionTypes?.Any(type =>
            type == typeof(OperationCanceledException).FullName
            || type == typeof(TaskCanceledException).FullName
            || type == typeof(OperationCanceledException).Name
            || type == typeof(TaskCanceledException).Name) == true;
}
