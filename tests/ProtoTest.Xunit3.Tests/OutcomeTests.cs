namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;
using Xunit;

public sealed class OutcomeTests
{
    [Fact]
    public void PassingSubject_ShouldRecordSucceededOutcome()
    {
        var trace = RunSubject(nameof(Subjects.Passing), TestResultState.ForPassed(0m));

        Assert.Equal(ProtoTraceOutcome.Succeeded, trace.Outcome);
    }

    [Fact]
    public void FailingSubject_ShouldRecordFailedOutcomeWithTheFailure()
    {
        var trace = RunSubject(
            nameof(Subjects.Failing),
            TestResultState.FromException(0m, new InvalidOperationException("deliberate failure")));

        Assert.Equal(ProtoTraceOutcome.Failed, trace.Outcome);
        Assert.Contains("deliberate failure", trace.Error?.Message);
    }

    [Fact]
    public void SkippedSubject_ShouldRecordSkippedOutcome()
    {
        var trace = RunSubject(nameof(Subjects.Skipped), TestResultState.ForSkipped(0m));

        Assert.Equal(ProtoTraceOutcome.Skipped, trace.Outcome);
    }

    // Drives the real attribute and the outcome mapping out of band, so a failing subject cannot fail the suite.
    private static ProtoTestTrace RunSubject(string subjectName, TestResultState state)
    {
        var method = typeof(Subjects).GetMethod(subjectName)!;

        new ProtoTestFactAttribute().Before(method, null!);
        ProtoTestLifecycleHandler.Complete(method, state);

        var name = ProtoTestName.FromMethod(method);
        return ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == name);
    }

    // Private, so xUnit's own discovery ignores these; their outcomes are recorded through the attribute above.
#pragma warning disable xUnit1000
    private sealed class Subjects
    {
        [ProtoTestFact]
        public void Passing()
        {
        }

        [ProtoTestFact]
        public void Failing() => Assert.Fail("deliberate failure");

        [ProtoTestFact]
        public void Skipped() => Assert.Skip("deliberate skip");
    }
#pragma warning restore xUnit1000
}
