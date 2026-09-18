namespace ProtoTest.Xunit.Tests;

using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
public sealed class SkipConditionTests
{
    [ProtoTestFact]
    [RequiresCapability("not-composed", Reason = "the adapter proves the skip path")]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
