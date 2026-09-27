namespace ProtoTest.NUnit.AutoWrap.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.NUnit;

/// <summary>
/// The auto-wrap contract: plain <c>[Test]</c> methods get the full lifecycle, a skip condition still
/// reports through NUnit before the lifecycle starts, and an explicit <c>[ProtoTest]</c> stays
/// single-wrapped.
/// </summary>
[Tracking("Class", Order = 10)]
public sealed class PlainTests
{
    [Test]
    [Tracking("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
        => AdapterLifecycle.VerifyTestBody<PlainTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));

    [Test]
    public async Task PlainTest_ShouldRunInsideAContextAcrossAwait()
    {
        var testName = Proto.Context.TestName;

        await Task.Yield();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(testName, Does.Contain(nameof(PlainTest_ShouldRunInsideAContextAcrossAwait)));
            Assert.That(Proto.Context.Service<ITestService>().GetMessage(), Is.EqualTo("ProtoTest_NUnit_AutoWrap_Success"));
        }
    }

    [Test]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public void SkippedPlainTest_ShouldNotRunItsBody()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [Test]
    [ProtoTest]
    [Tracking("Method", Order = 20)]
    public void ExplicitProtoTestAttribute_ShouldStillBeWrappedOnce()
        => AdapterLifecycle.VerifyTestBody<PlainTests>(nameof(ExplicitProtoTestAttribute_ShouldStillBeWrappedOnce));
}
