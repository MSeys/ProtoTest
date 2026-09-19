namespace ProtoTest.NUnit.Tests;

using global::NUnit.Framework.Internal;
using global::NUnit.Framework.Interfaces;
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

    private static ProtoTestTrace RunSubject(string subjectName, ResultState state, string? message = null)
    {
        var method = new TestMethod(new MethodWrapper(typeof(Subjects), subjectName));
        var attribute = new ProtoTestAttribute();

        attribute.BeforeTest(method);

        var result = method.MakeTestResult();
        if (message is null)
        {
            result.SetResult(state);
        }
        else
        {
            result.SetResult(state, message, "deliberate stack trace");
        }

        // Stand in for the NUnit runner: AfterTest reads the outcome from the ambient execution context.
        var context = TestExecutionContext.CurrentContext;
        var previousResult = context.CurrentResult;
        context.CurrentResult = result;
        try
        {
            attribute.AfterTest(method);
        }
        finally
        {
            context.CurrentResult = previousResult;
        }

        var name = ProtoTestName.FromMethod(typeof(Subjects).GetMethod(subjectName)!);
        return ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == name);
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
    }
}
