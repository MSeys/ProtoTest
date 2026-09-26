namespace ProtoTest.TUnit.Tests;

using global::TUnit.Core.Exceptions;
using global::TUnit.Core.Executors;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

public sealed class SkipConditionTests
{
    [Test]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public Task RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [Test]
    [TestExecutor<PassthroughTestExecutor>]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public async Task SkipCondition_ShouldRaiseTheTUnitSkipSignalBeforeTheLifecycle()
    {
        // The driver's own condition makes the real executor take its skip path; the signal it raises
        // is the one TUnit turns into a skipped test. A regression that starts the lifecycle and runs
        // the body fails on the body flag, and one that reports the test as passed fails on the signal.
        var bodyRan = false;
        SkipTestException? signal = null;
        try
        {
            await new ProtoTestExecutor().ExecuteTest(TestContext.Current!, () =>
            {
                bodyRan = true;
                return default;
            });
        }
        catch (SkipTestException exception)
        {
            signal = exception;
        }

        await Assert.That(signal).IsNotNull();
        await Assert.That(signal!.Reason).Contains(AdapterProbes.SkipReason);
        await Assert.That(bodyRan).IsFalse();
        // The skip happens before the lifecycle starts, so no trace was opened for it.
        var name = ProtoTestName.FromMethod(typeof(SkipConditionTests).GetMethod(
            nameof(SkipCondition_ShouldRaiseTheTUnitSkipSignalBeforeTheLifecycle))!);
        await Assert.That(ProtoTestAssembly.Host.Trace.Snapshot().Tests.Any(test => test.Name == name)).IsFalse();
    }
}
