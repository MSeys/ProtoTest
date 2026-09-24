namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;

[Tracking("Class", Order = 10)]
public sealed class AdapterComplianceTests
{
    [ProtoTestFact]
    [Tracking("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
    {
        AdapterLifecycle.VerifyTestBody<AdapterComplianceTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));
    }
}
