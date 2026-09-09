namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;

[AdapterContract("Class", Order = 10)]
public sealed class AdapterComplianceTests
{
    [ProtoTestFact]
    [AdapterContract("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
    {
        AdapterContract.VerifyTestBody<AdapterComplianceTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));
    }
}
