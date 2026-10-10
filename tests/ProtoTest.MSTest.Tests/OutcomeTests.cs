namespace ProtoTest.MSTest.Tests;

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestClass]
public sealed class OutcomeTests
{
    [TestMethod]
    public async Task PassingSubject_ShouldRecordSucceededOutcome()
    {
        var trace = await RunSubjectAsync(nameof(Subjects.Passing));

        Assert.AreEqual(ProtoTraceOutcome.Succeeded, trace.Outcome);
    }

    [TestMethod]
    public async Task FailingSubject_ShouldRecordFailedOutcomeWithTheAssertion()
    {
        var trace = await RunSubjectAsync(nameof(Subjects.Failing));

        Assert.AreEqual(ProtoTraceOutcome.Failed, trace.Outcome);
        StringAssert.Contains(trace.Error?.Message, "deliberate failure");
    }

    [TestMethod]
    public async Task InconclusiveSubject_ShouldRecordSkippedOutcome()
    {
        var trace = await RunSubjectAsync(nameof(Subjects.Inconclusive));

        Assert.AreEqual(ProtoTraceOutcome.Skipped, trace.Outcome);
    }

    [TestMethod]
    public async Task CapabilitySkip_ShouldReturnAnIgnoredResultCarryingTheReason()
    {
        var method = typeof(Subjects).GetMethod(nameof(Subjects.RequiresCapability))!;

        var results = await new ProtoTestAttribute().ExecuteAsync(new FakeTestMethod(method));

        Assert.HasCount(1, results);
        Assert.AreEqual(UnitTestOutcome.Ignored, results[0].Outcome);
        StringAssert.Contains(results[0].LogOutput, "the adapter proves the skip path");
        StringAssert.Contains(results[0].DisplayName, "the adapter proves the skip path");

        // The skip happens before the lifecycle starts, so nothing is recorded.
        var name = ProtoTestName.FromMethod(method);
        Assert.IsFalse(ProtoTestAssembly.Host.Trace.Snapshot().Tests.Any(test => test.Name == name));
    }

    [TestMethod]
    public void CancelledRow_ShouldRecordCancelledOutcome()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(new TestResult
        {
            Outcome = UnitTestOutcome.Failed,
            TestFailureException = new OperationCanceledException("deliberate cancellation")
        });

        Assert.AreEqual(ProtoTraceOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    public void CancelledRowInMSTestsWrapper_ShouldRecordCancelledOutcome()
    {
        // MSTest hands the adapter what the test method threw inside its own TestFailedException, an internal
        // type; the adapter recognizes it by name, which the runner contract suite checks against the real runner.
        var result = ProtoTestAttribute.ToProtoTestResult(new TestResult
        {
            Outcome = UnitTestOutcome.Failed,
            TestFailureException = new TestFailedException(new OperationCanceledException("deliberate cancellation"))
        });

        Assert.AreEqual(ProtoTraceOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    public void TimedOutRow_ShouldRecordCancelledOutcomeWithQualifiedErrorType()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(new TestResult { Outcome = UnitTestOutcome.Timeout });

        Assert.AreEqual(ProtoTraceOutcome.Cancelled, result.Outcome);
        Assert.AreEqual("MSTest.Timeout", result.Error?.Type);
    }

    [TestMethod]
    public void AbortedRow_ShouldRecordCancelledOutcome()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(new TestResult { Outcome = UnitTestOutcome.Aborted });

        Assert.AreEqual(ProtoTraceOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    public void NotRunnableRow_ShouldRecordSkippedOutcome()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(new TestResult { Outcome = UnitTestOutcome.NotRunnable });

        Assert.AreEqual(ProtoTraceOutcome.Skipped, result.Outcome);
    }

    [TestMethod]
    public void FailedRowWithoutException_ShouldRecordQualifiedErrorType()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(new TestResult { Outcome = UnitTestOutcome.Error });

        Assert.AreEqual(ProtoTraceOutcome.Failed, result.Outcome);
        Assert.AreEqual("MSTest.Error", result.Error?.Type);
    }

    private static async Task<ProtoTestTrace> RunSubjectAsync(string subjectName)
    {
        var method = typeof(Subjects).GetMethod(subjectName)!;

        await new ProtoTestAttribute().ExecuteAsync(new FakeTestMethod(method));

        var name = ProtoTestName.FromMethod(method);
        return ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == name);
    }

    // Private, so MSTest's own discovery ignores these; they only run through the fake ITestMethod below.
    // The analyzer cannot see that deliberate indirection, and Passing's constant assertion is the
    // controlled successful outcome consumed by the adapter test rather than an assertion under test.
#pragma warning disable MSTEST0030, MSTEST0032
    private sealed class Subjects
    {
        [ProtoTest]
        public void Passing() => Assert.AreEqual(2, 1 + 1);

        [ProtoTest]
        public void Failing() => Assert.Fail("deliberate failure");

        [ProtoTest]
        public void Inconclusive() => Assert.Inconclusive("deliberate inconclusive");

        [ProtoTest]
        [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
        public void RequiresCapability() => throw new InvalidOperationException("A skipped test must not run its body.");
    }
#pragma warning restore MSTEST0030, MSTEST0032

    private sealed class TestFailedException(Exception thrown) : Exception("Test method threw an exception.", thrown);
}
