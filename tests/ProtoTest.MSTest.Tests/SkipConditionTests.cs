namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;
using ProtoTest.MSTest;

[TestClass]
public sealed class SkipConditionTests
{
    [ProtoTest]
    [RequiresCapability("not-composed", Reason = "the adapter proves the skip path")]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
