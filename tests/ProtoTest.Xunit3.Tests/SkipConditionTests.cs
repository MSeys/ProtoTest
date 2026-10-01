namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using Xunit;
using Xunit.Sdk;

public sealed class SkipConditionTests
{
    [ProtoTestFact]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [Fact]
    public void SkipCondition_ShouldRaiseTheXunitSkipSignalBeforeTheLifecycle()
    {
        // The real runner turns Assert.Skip into a skipped test; this driver pins the signal the
        // adapter emits (xUnit's own dynamic-skip exception, reason included) and that nothing was
        // started. Record.Exception deliberately lets a skip escape - it would report this driver as
        // skipped - so the driver catches it. A regression that reports the test as passed fails
        // here; one that drops the test from discovery is caught by the project's declared test
        // minimum in ./proto test.
        var method = typeof(Subjects).GetMethod(nameof(Subjects.RequiresCapability))!;

        SkipException? signal = null;
        try
        {
            ProtoTestLifecycleHandler.Before(method, test: null);
        }
        catch (SkipException exception)
        {
            signal = exception;
        }

        Assert.NotNull(signal);
        Assert.Contains(AdapterProbes.SkipReason, signal!.Message);
        // The skip happens before the lifecycle starts, so no trace was opened for it.
        var name = ProtoTestName.FromMethod(method);
        Assert.DoesNotContain(ProtoTestAssembly.Host.Trace.Snapshot().Tests, test => test.Name == name);
    }

    // Private, so xUnit's own discovery ignores the driver subject; the driver drives it directly.
#pragma warning disable xUnit1000
    private sealed class Subjects
    {
        [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
        public void RequiresCapability() => throw new InvalidOperationException("A skipped test must not run its body.");
    }
#pragma warning restore xUnit1000
}
