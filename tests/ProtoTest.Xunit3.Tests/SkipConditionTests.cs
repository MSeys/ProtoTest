namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;

public sealed class SkipConditionTests
{
    [ProtoTestFact]
    [RequiresCapability("not-composed", Reason = "the adapter proves the skip path")]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
