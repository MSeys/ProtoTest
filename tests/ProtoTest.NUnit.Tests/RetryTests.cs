namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

/// <summary>
/// A retried test attempt is its own lifecycle: the trace records one entry per attempt, so a suite
/// can tell a flaky pass from a first-time pass.
/// </summary>
[TestFixture]
public sealed class RetryTests
{
    private static int _attempts;

    [ProtoTest]
    [Retry(2)]
    public void RetriedCase_ShouldRecordEachAttempt()
    {
        if (Interlocked.Increment(ref _attempts) == 1)
        {
            Assert.Fail("deliberate first-attempt failure");
        }
    }

    [OneTimeTearDown]
    public static void EachAttempt_ShouldHaveRecordedItsOwnTrace()
    {
        var name = ProtoTestName.FromMethod(
            typeof(RetryTests).GetMethod(nameof(RetriedCase_ShouldRecordEachAttempt))!);
        var traces = ProtoTestAssembly.Host.Trace.Snapshot().Tests
            .Where(test => test.Name == name)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(traces, Has.Length.EqualTo(2), "each retry attempt is its own lifecycle record");
            Assert.That(
                traces.Select(test => test.Outcome),
                Is.EquivalentTo(new[] { ProtoTraceOutcome.Failed, ProtoTraceOutcome.Succeeded }));
        });
    }
}
