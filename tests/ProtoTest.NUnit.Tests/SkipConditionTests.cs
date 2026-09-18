namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class SkipConditionTests
{
    [ProtoTest]
    [RequiresCapability("not-composed", Reason = "the adapter proves the skip path")]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [ProtoTest]
    [RequiresInProcess]
    public void RequiresInProcess_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
