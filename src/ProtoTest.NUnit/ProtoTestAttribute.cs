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
        return WrapCommand(command);
    }

    /// <summary>
    /// The one command wrapper the lifecycle entry points share: the test attribute and the assembly
    /// auto-wrap both run a test through this, so their lifecycle, skip path and result mapping cannot
    /// drift apart.
    /// </summary>
    internal static TestCommand WrapCommand(TestCommand command)
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
                ProtoTestScope.StartAsync(
                    preparation,
                    ProtoTestAssembly.Host,
                    NUnitAttachmentPublisher.Instance,
                    context.CancellationToken)));
            Exception? failure = null;
            try
            {
                return innerCommand.Execute(context);
            }
            catch (Exception exception)
            {
                // A fixture without setup or teardown has no command that records a body exception, so
                // the exception leaves before NUnit writes it to the result; the work item records it
                // only after this scope completed. Keep it, unwrapped as NUnit would, so the trace maps
                // the failure NUnit will report instead of the result's initial Inconclusive state.
                failure = exception.Unwrap();
                throw;
            }
            finally
            {
                scope.Result = MapResult(context, failure);
                ProtoTestAsync.RunSync(() => scope.DisposeAsync());
            }
        }
    }

    /// <summary>
    /// Maps the result NUnit recorded, or the exception that escaped before NUnit could record it.
    /// NUnit's result carries no exception object, so for a recorded failure the shared
    /// <see cref="ProtoTestResult.FromException"/> rule is deliberately not applied: a cancelled body
    /// reads as Failed because NUnit reports it as a plain failure (pinned by
    /// <c>OutcomeTests.CancelledSubject_ShouldRecordFailedOutcomeBecauseNUnitExposesNoExceptionType</c>).
    /// </summary>
    private static ProtoTestResult MapResult(TestExecutionContext context, Exception? failure)
    {
        var nunitResult = context.CurrentResult;
        var state = failure switch
        {
            ResultStateException resultStateException => resultStateException.ResultState,
            not null => ResultState.Error,
            _ => nunitResult.ResultState
        };
        return state.Status switch
        {
            TestStatus.Passed => ProtoTestResult.Passed,
            TestStatus.Failed when failure is not null => ProtoTestResult.Failed(new ProtoTraceError(
                failure.GetType().FullName ?? failure.GetType().Name,
                failure.Message,
                failure.StackTrace)),
            TestStatus.Failed => ProtoTestResult.Failed(
                "NUnit",
                string.IsNullOrEmpty(state.Label) ? TestStatus.Failed.ToString() : state.Label,
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
