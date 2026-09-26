namespace ProtoTest.Core.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class ProtoTestResultTests
{
    [Test]
    public void FromException_ShouldClassifyCancellationAndFailureWithOneRule()
    {
        // The one classifier the runner adapters share: OCE and its
        // TaskCanceledException subclass are cancellation, anything else is a failure.
        Assert.Multiple(() =>
        {
            Assert.That(ProtoTestResult.FromException(new OperationCanceledException()).Outcome, Is.EqualTo(ProtoTraceOutcome.Cancelled));
            Assert.That(ProtoTestResult.FromException(new TaskCanceledException()).Outcome, Is.EqualTo(ProtoTraceOutcome.Cancelled));
            Assert.That(ProtoTestResult.FromException(new InvalidOperationException()).Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        });
    }

    [Test]
    public void IsCancellation_ShouldMatchRunnerReportedTypeNames()
    {
        // xUnit v3 reports the exception type as a name, not as an object; the name half of the rule
        // must accept full names and simple names for both cancellation types.
        Assert.Multiple(() =>
        {
            Assert.That(ProtoTestResult.IsCancellation("System.OperationCanceledException"), Is.True);
            Assert.That(ProtoTestResult.IsCancellation("System.Threading.Tasks.TaskCanceledException"), Is.True);
            Assert.That(ProtoTestResult.IsCancellation("OperationCanceledException"), Is.True);
            Assert.That(ProtoTestResult.IsCancellation("TaskCanceledException"), Is.True);
            Assert.That(ProtoTestResult.IsCancellation("System.InvalidOperationException"), Is.False);
            Assert.That(ProtoTestResult.IsCancellation(null), Is.False);
        });
    }
}
