namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;

/// <summary>
/// The xUnit v3 runner's per-test token rides the lifecycle: the adapter passes
/// <c>TestContext.Current.CancellationToken</c>, so the body sees it on
/// <c>Proto.Context.CancellationToken</c>.
/// </summary>
public sealed class CancellationTokenTests
{
    [ProtoTestFact]
    public void RunnerToken_ShouldReachTheLifecycle()
        => Assert.True(
            Proto.Context.CancellationToken.CanBeCanceled,
            "the xUnit v3 adapter passes TestContext.Current.CancellationToken");
}
