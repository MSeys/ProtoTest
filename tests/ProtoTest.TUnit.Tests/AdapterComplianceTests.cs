namespace ProtoTest.TUnit.Tests;

using ProtoTest.AdapterContract;

[Tracking("Class", Order = 10)]
public sealed class AdapterComplianceTests
{
    [Test]
    [Tracking("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
    {
        AdapterLifecycle.VerifyTestBody<AdapterComplianceTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));
    }
}
