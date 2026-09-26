namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.MSTest;

[TestClass]
public sealed class SkipConditionTests
{
    [ProtoTest]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public void RequiresCapability_ShouldSkipBeforeTheLifecycle()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
