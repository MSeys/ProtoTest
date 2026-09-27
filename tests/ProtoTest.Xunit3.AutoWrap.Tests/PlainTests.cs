namespace ProtoTest.Xunit3.AutoWrap.Tests;

using System.Collections.Concurrent;
using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.Xunit3;
using Xunit;

/// <summary>
/// The auto-wrap contract: plain <c>[Fact]</c> and <c>[Theory]</c> methods get the full lifecycle, a
/// skip condition still reports through xUnit before the lifecycle starts, and an explicit
/// <c>[ProtoTestFact]</c> stays single-wrapped.
/// </summary>
[Tracking("Class", Order = 10)]
public sealed class PlainTests
{
    private static readonly ConcurrentDictionary<string, byte> SeenRowContexts = new(StringComparer.Ordinal);

    [Fact]
    [Tracking("Method", Order = 20)]
    public void Adapter_ShouldSatisfySharedLifecycleContract()
        => AdapterLifecycle.VerifyTestBody<PlainTests>(nameof(Adapter_ShouldSatisfySharedLifecycleContract));

    [Fact]
    public async Task PlainFact_ShouldRunInsideAContextAcrossAwait()
    {
        var testName = Proto.Context.TestName;

        await Task.Yield();

        Assert.Contains(nameof(PlainFact_ShouldRunInsideAContextAcrossAwait), testName);
        Assert.Equal("ProtoTest_Xunit3_AutoWrap_Success", Proto.Context.Service<ITestService>().GetMessage());
    }

    /// <summary>
    /// xUnit wraps every theory row like a fact: each row starts its own lifecycle under its own
    /// display name, and the adapter maps that row's outcome.
    /// </summary>
    [Theory]
    [InlineData("alpha", 1)]
    [InlineData("beta", 2)]
    public void PlainTheory_ShouldRunEachRowInItsOwnContext(string label, int value)
    {
        var context = Proto.Context;

        Assert.True(
            SeenRowContexts.TryAdd(context.TestId, 0),
            $"Row '{label}' reused lifetime '{context.TestId}'; auto-wrap must start a context per row.");
        Assert.Contains(nameof(PlainTheory_ShouldRunEachRowInItsOwnContext), context.TestName);
        Assert.Contains(label, context.TestName);
        Assert.Equal("ProtoTest_Xunit3_AutoWrap_Success", context.Service<ITestService>().GetMessage());
        Assert.True(value > 0);
    }

    [Fact]
    [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
    public void SkippedPlainFact_ShouldNotRunItsBody()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [ProtoTestFact]
    [Tracking("Method", Order = 20)]
    public void ExplicitProtoTestFact_ShouldStillBeWrappedOnce()
        => AdapterLifecycle.VerifyTestBody<PlainTests>(nameof(ExplicitProtoTestFact_ShouldStillBeWrappedOnce));
}
