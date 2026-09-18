namespace ProtoTest.TUnit.Tests;

using ProtoTest.Core;

public sealed class SkipConditionTests
{
    [Test]
    [RequiresCapability("not-composed", Reason = "the adapter proves the skip path")]
    public Task RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
