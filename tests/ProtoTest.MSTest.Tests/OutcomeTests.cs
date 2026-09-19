namespace ProtoTest.MSTest.Tests;

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual(UnitTestOutcome.Ignored, results[0].Outcome);
        StringAssert.Contains(results[0].LogOutput, "the adapter proves the skip path");
        StringAssert.Contains(results[0].DisplayName, "the adapter proves the skip path");

        // The skip happens before the lifecycle starts, so nothing is recorded.
        var name = ProtoTestName.FromMethod(method);
        Assert.IsFalse(ProtoTestAssembly.Host.Trace.Snapshot().Tests.Any(test => test.Name == name));
    }

    [TestMethod]
    public void MixedPassedAndIgnoredRows_ShouldRecordPartialOutcome()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(
        [
            new TestResult { Outcome = UnitTestOutcome.Passed },
            new TestResult { Outcome = UnitTestOutcome.Ignored }
        ]);

        Assert.AreEqual(ProtoTraceOutcome.Partial, result.Outcome);
    }

    [TestMethod]
    public void MixedPassedAndInconclusiveRows_ShouldRecordPartialOutcome()
    {
        var result = ProtoTestAttribute.ToProtoTestResult(
        [
            new TestResult { Outcome = UnitTestOutcome.Passed },
            new TestResult { Outcome = UnitTestOutcome.Inconclusive }
        ]);

        Assert.AreEqual(ProtoTraceOutcome.Partial, result.Outcome);
    }

    private static async Task<ProtoTestTrace> RunSubjectAsync(string subjectName)
    {
        var method = typeof(Subjects).GetMethod(subjectName)!;

        await new ProtoTestAttribute().ExecuteAsync(new FakeTestMethod(method));

        var name = ProtoTestName.FromMethod(method);
        return ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == name);
    }

    // Private, so MSTest's own discovery ignores these; they only run through the fake ITestMethod below.
    private sealed class Subjects
    {
        [ProtoTest]
        public void Passing() => Assert.AreEqual(2, 1 + 1);

        [ProtoTest]
        public void Failing() => Assert.Fail("deliberate failure");

        [ProtoTest]
        public void Inconclusive() => Assert.Inconclusive("deliberate inconclusive");

        [ProtoTest]
        [RequiresCapability("not-composed", Reason = "the adapter proves the skip path")]
        public void RequiresCapability() => throw new InvalidOperationException("A skipped test must not run its body.");
    }

    /// <summary>
    /// Stands in for the MSTest runner: it invokes the subject like the framework would and turns the
    /// outcome into a <see cref="TestResult"/>, so the adapter's mapping code runs for real.
    /// </summary>
    private sealed class FakeTestMethod(MethodInfo method) : ITestMethod
    {
        public string TestMethodName => method.Name;

        public string TestClassName => method.DeclaringType!.FullName!;

        public Type ReturnType => method.ReturnType;

        public object?[]? Arguments => null;

        public ParameterInfo[] ParameterTypes => method.GetParameters();

        public MethodInfo MethodInfo => method;

        public Attribute[] GetAllAttributes() => method.GetCustomAttributes().ToArray();

        public TAttributeType[] GetAttributes<TAttributeType>() where TAttributeType : Attribute
            => method.GetCustomAttributes<TAttributeType>().ToArray();

        public async Task<TestResult> InvokeAsync(object?[]? arguments)
        {
            try
            {
                var instance = Activator.CreateInstance(method.DeclaringType!);
                var returned = method.Invoke(instance, arguments);
                if (returned is Task task)
                {
                    await task;
                }

                return new TestResult { Outcome = UnitTestOutcome.Passed };
            }
            catch (Exception exception)
            {
                // Reflection wraps the assertion failure, exactly like a runner that inspects the inner exception.
                var failure = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                return failure is AssertInconclusiveException
                    ? new TestResult { Outcome = UnitTestOutcome.Inconclusive, TestFailureException = failure }
                    : new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = failure };
            }
        }
    }
}
