namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

/// <summary>
/// NUnit's cancellation token rides the lifecycle: a runner-driven test reads the same token the
/// adapter passed to <c>StartTestAsync</c> from <c>Proto.Context.CancellationToken</c>, and NUnit's
/// <c>[CancelAfter]</c> cancels it while the body runs.
/// </summary>
[TestFixture]
public sealed class CancellationTests
{
    [Test]
    [ProtoTest]
    [CancelAfter(500)]
    public async Task RunnerCancellation_ShouldReachTheTestToken()
    {
        var token = Proto.Context.CancellationToken;
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException)
        {
            // [CancelAfter] cancels the runner's token; the body observes the cancellation.
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(token.CanBeCanceled, Is.True,
                "the NUnit adapter supplies TestExecutionContext.CancellationToken");
            Assert.That(token.IsCancellationRequested, Is.True,
                "the [CancelAfter] token reached Proto.Context.CancellationToken");
            Assert.That(bound.IsCancellationRequested, Is.False,
                "the test ended when the runner's token fired, not at its own bound");
        }
    }
}
