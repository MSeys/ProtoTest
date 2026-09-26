namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using global::NUnit.Framework.Interfaces;
using global::NUnit.Framework.Internal;
using global::NUnit.Framework.Internal.Commands;
using ProtoTest.Core;

/// <summary>
/// NUnit test attribute that manages the <see cref="ProtoExecutionContext"/> lifecycle for each test.
/// The lifecycle is a command wrapper applied outside NUnit's setup and teardown, so it spans
/// <c>[SetUp]</c> and <c>[TearDown]</c>, and a skip condition is decided before <c>[SetUp]</c> runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ProtoTestAttribute : TestAttribute, IWrapSetUpTearDown
{
    /// <summary>Wraps the test so the lifecycle encloses setup, the body and teardown.</summary>
    public TestCommand Wrap(TestCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new ProtoTestCommand(command);
    }

    /// <summary>
    /// Runs one test's lifecycle: skip before anything runs, otherwise start the scope, run NUnit's
    /// command pipeline, and complete the scope with the result NUnit recorded.
    /// </summary>
    private sealed class ProtoTestCommand(TestCommand innerCommand) : DelegatingTestCommand(innerCommand)
    {
        public override TestResult Execute(TestExecutionContext context)
        {
            var test = Test;

            // The case's full name distinguishes parameterized rows while staying the stable fully
            // qualified name for a plain method.
            var preparation = ProtoTestAdapter.Prepare(
                test.Method!.MethodInfo,
                ProtoTestAssembly.Host,
                test.FullName);

            if (!preparation.CanRun)
            {
                // Nothing has run yet, not even [SetUp]; reporting the skip here keeps the lifecycle
                // and the trace honest.
                context.CurrentResult.SetResult(ResultState.Ignored, preparation.SkipReason!);
                return context.CurrentResult;
            }

            var scope = ProtoTestAsync.RunSync(() => new ValueTask<ProtoTestScope>(
                ProtoTestScope.StartAsync(preparation, ProtoTestAssembly.Host, NUnitAttachmentPublisher.Instance)));
            try
            {
                return innerCommand.Execute(context);
            }
            finally
            {
                scope.Result = MapResult(context);
                ProtoTestAsync.RunSync(() => scope.DisposeAsync());
            }
        }
    }

    /// <summary>
    /// Maps the result NUnit recorded. NUnit exposes only its own result state - the exception object
    /// never reaches the adapter - so the shared <see cref="ProtoTestResult.FromException"/> rule is
    /// deliberately not applied here: a cancelled body reads as Failed because NUnit reports it as a
    /// plain failure (pinned by <c>OutcomeTests.CancelledSubject_ShouldRecordFailedOutcomeBecauseNUnitExposesNoExceptionType</c>).
    /// </summary>
    private static ProtoTestResult MapResult(TestExecutionContext context)
    {
        var nunitResult = context.CurrentResult;
        var state = nunitResult.ResultState;
        return state.Status switch
        {
            TestStatus.Passed => ProtoTestResult.Passed,
            TestStatus.Failed => ProtoTestResult.Failed(
                "NUnit",
                state.Label ?? TestStatus.Failed.ToString(),
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
