namespace ProtoTest.NUnit.Tests;

using ProtoTest.AdapterContract;

[TestFixture]
[Tracking("Class", Order = 10)]
public sealed class AdapterComplianceTests
{
    [ProtoTest]
    [Tracking("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
    {
        AdapterLifecycle.VerifyTestBody<AdapterComplianceTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));
    }
}
