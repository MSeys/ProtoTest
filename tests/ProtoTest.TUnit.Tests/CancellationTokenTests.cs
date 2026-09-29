namespace ProtoTest.TUnit.Tests;

using ProtoTest.Core;

/// <summary>
/// TUnit's per-test cancellation token rides the lifecycle: the executor passes
/// <c>TestContext.Execution.CancellationToken</c>, so the body sees it on
/// <c>Proto.Context.CancellationToken</c>.
/// </summary>
public sealed class CancellationTokenTests
{
    [Test]
    public async Task RunnerToken_ShouldReachTheLifecycle()
    {
        var runnerToken = global::TUnit.Core.TestContext.Current!.Execution.CancellationToken;

        await Assert.That(runnerToken.CanBeCanceled).IsTrue();
        await Assert.That(Proto.Context.CancellationToken.CanBeCanceled).IsTrue();
    }
}
