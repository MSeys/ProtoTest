namespace ProtoTest.NUnit.Tests;

using global::NUnit.Framework.Interfaces;
using global::NUnit.Framework.Internal;
using global::NUnit.Framework.Internal.Commands;
using ProtoTest.Core;

[TestFixture]
public sealed class OutcomeTests
{
    [Test]
    public void PassingSubject_ShouldRecordSucceededOutcome()
    {
        var trace = RunSubject(nameof(Subjects.Passing), ResultState.Success);

        Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
    }

    [Test]
    public void FailingSubject_ShouldRecordFailedOutcomeWithTheAssertion()
    {
        var trace = RunSubject(nameof(Subjects.Failing), ResultState.Failure, "deliberate failure");

        Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        Assert.That(trace.Error?.Message, Does.Contain("deliberate failure"));
        Assert.That(
            trace.Error?.StackTrace,
            Does.Contain("deliberate stack trace"),
            "the runner's stack trace survives into the trace");
    }

    [Test]
    public void SkippedSubject_ShouldRecordSkippedOutcome()
    {
        var trace = RunSubject(nameof(Subjects.Skipped), ResultState.Skipped, "deliberate skip");

        Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Skipped));
    }

    [Test]
    public void InconclusiveSubject_ShouldRecordSkippedOutcome()
    {
        var trace = RunSubject(nameof(Subjects.Inconclusive), ResultState.Inconclusive, "deliberate inconclusive");

        Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Skipped));
    }

    [Test]
    public void WarningSubject_ShouldRecordPartialOutcome()
    {
        var trace = RunSubject(nameof(Subjects.Warning), ResultState.Warning, "deliberate warning");

        Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
    }

    [Test]
    public void CancelledSubject_ShouldRecordFailedOutcomeBecauseNUnitExposesNoExceptionType()
    {
        // NUnit's result carries no exception object, only a message and stack trace, so the shared
        // cancellation rule cannot be applied by the adapter; a cancelled test records Failed.
        var trace = RunSubject(nameof(Subjects.Cancelled), ResultState.Failure, "deliberate cancellation");

        Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
    }

    private static ProtoTestTrace RunSubject(string subjectName, ResultState state, string? message = null)
    {
        var subject = new TestMethod(new MethodWrapper(typeof(Subjects), subjectName));
        var command = new ProtoTestAttribute().Wrap(new ResultCommand(subject, state, message));

        // Stand in for the NUnit runner: the wrapper reads the result from the ambient execution context.
        var context = TestExecutionContext.CurrentContext;
        var previousResult = context.CurrentResult;
        context.CurrentResult = subject.MakeTestResult();
        try
        {
            command.Execute(context);
        }
        finally
        {
            context.CurrentResult = previousResult;
        }

        return ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == subject.FullName);
    }

    /// <summary>Stands in for the test body: records the state the subject would have produced.</summary>
    private sealed class ResultCommand(Test subject, ResultState state, string? message) : TestCommand(subject)
    {
        public override TestResult Execute(TestExecutionContext context)
        {
            if (message is null)
            {
                context.CurrentResult.SetResult(state);
            }
            else
            {
                context.CurrentResult.SetResult(state, message, "deliberate stack trace");
            }

            return context.CurrentResult;
        }
    }

    // Private, so NUnit's own discovery ignores these; their outcomes are recorded through the attribute above.
    private sealed class Subjects
    {
        public void Passing()
        {
        }

        public void Failing() => Assert.Fail("deliberate failure");

        public void Skipped() => Assert.Ignore("deliberate skip");

        public void Inconclusive() => Assert.Inconclusive("deliberate inconclusive");

        public void Warning() => Assert.Warn("deliberate warning");

        public void Cancelled() => throw new OperationCanceledException("deliberate cancellation");
    }
}
