namespace ProtoTest.NUnit.Tests;

using global::NUnit.Framework.Interfaces;
using global::NUnit.Framework.Internal;
using global::NUnit.Framework.Internal.Commands;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestFixture]
public sealed class SkipConditionTests
{
    [ProtoTest]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [ProtoTest]
    [RequiresInProcess]
    public void RequiresInProcess_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [Test]
    public void SkipCondition_ShouldReportIgnoredToNUnitBeforeTheLifecycle()
    {
        // The real runner executes the tests above; this driver pins the state the adapter hands it:
        // NUnit's own result is Ignored with the reason, and the body never runs. A regression that
        // reports the test as passed (or drops the lifecycle silently) fails here.
        var subject = new TestMethod(new MethodWrapper(typeof(Subjects), nameof(Subjects.RequiresCapability)));
        var inner = new ProbeCommand(subject);
        var command = new ProtoTestAttribute().Wrap(inner);

        // Stand in for the NUnit runner: the wrapper reads the result from the ambient execution context.
        var context = TestExecutionContext.CurrentContext;
        var previousResult = context.CurrentResult;
        context.CurrentResult = subject.MakeTestResult();
        try
        {
            var result = command.Execute(context);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultState, Is.EqualTo(ResultState.Ignored));
                Assert.That(result.Message, Does.Contain(AdapterProbes.SkipReason));
                Assert.That(inner.Executed, Is.False, "a skipped test must not run its body");
                // The skip happens before the lifecycle starts, so no trace was opened for it.
                Assert.That(
                    ProtoTestAssembly.Host.Trace.Snapshot().Tests.Any(test => test.Name == subject.FullName),
                    Is.False);
            });
        }
        finally
        {
            context.CurrentResult = previousResult;
        }
    }

    /// <summary>Stands in for the test body and records whether NUnit ever reached it.</summary>
    private sealed class ProbeCommand(Test subject) : TestCommand(subject)
    {
        public bool Executed { get; private set; }

        public override TestResult Execute(TestExecutionContext context)
        {
            Executed = true;
            context.CurrentResult.SetResult(ResultState.Success);
            return context.CurrentResult;
        }
    }

    // Private, so NUnit's own discovery ignores the driver subject; the driver above wraps it directly.
    private sealed class Subjects
    {
        [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
        public void RequiresCapability() => throw new InvalidOperationException("A skipped test must not run its body.");
    }
}
