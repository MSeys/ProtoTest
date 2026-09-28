namespace ProtoTest.Xunit.Tests;

using ProtoTest.Core;

/// <summary>
/// The xUnit v2 runner's cancellation token rides the lifecycle: the adapter passes the case runner's
/// <c>CancellationTokenSource</c> token, so the body sees a cancellable token on
/// <c>Proto.Context.CancellationToken</c>.
/// </summary>
[Collection(ProtoTestCollection.Name)]
public sealed class CancellationTokenTests
{
    [ProtoTestFact]
    public void RunnerToken_ShouldReachTheLifecycle()
        => Assert.True(
            Proto.Context.CancellationToken.CanBeCanceled,
            "the xUnit v2 adapter passes the runner's CancellationTokenSource token");
}
