namespace ProtoTest.MSTest.Tests;

using ProtoTest.AdapterContract;

[TestClass]
[AdapterContract("Class", Order = 10)]
public sealed class AdapterComplianceTests
{
    [ProtoTest]
    [AdapterContract("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
    {
        AdapterContract.VerifyTestBody<AdapterComplianceTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));
    }
}
