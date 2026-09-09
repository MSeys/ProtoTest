namespace ProtoTest.Xunit.Tests;

using ProtoTest.AdapterContract;

[Collection(ProtoTestCollection.Name)]
[AdapterContract("Class", Order = 10)]
public sealed class AdapterComplianceTests
{
    [Fact]
    [ProtoTest]
    [AdapterContract("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
    {
        AdapterContract.VerifyTestBody<AdapterComplianceTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));
    }
}
