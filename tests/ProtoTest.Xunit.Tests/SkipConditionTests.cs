namespace ProtoTest.Xunit.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
public sealed class SkipConditionTests
{
    [ProtoTestFact]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
