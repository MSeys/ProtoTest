namespace ProtoTest.Core.Tests;

using ProtoTest.Core;

/// <summary>
/// Pins the one polling engine's interval and deadline behavior: readiness, web
/// assertions and every other wait ride <see cref="ProtoPolling"/>, so its guarantees have a test of
/// their own instead of being assumed through a consumer.
/// </summary>
[TestFixture]
public sealed class ProtoPollingTests
{
    [Test]
    public async Task PollAsync_ShouldWaitTheIntervalBetweenProbesAndStopNoEarlierThanTheDeadline()
    {
        var attempts = 0;
        var satisfied = await ProtoPolling.PollAsync(
            _ =>
            {
                attempts++;
                return ValueTask.FromResult(attempts >= 2);
            },
            value => value,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(satisfied.Satisfied, Is.True);
            Assert.That(attempts, Is.EqualTo(2), "the poll stops at the satisfying probe");
            // A wall-clock floor with timer slack: the scheduler may wake a few milliseconds early.
            Assert.That(
                satisfied.Elapsed,
                Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150)),
                "the retry waits out the interval instead of spinning");
        });

        var never = await ProtoPolling.PollAsync(
            _ => ValueTask.FromResult(false),
            value => value,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(never.Satisfied, Is.False);
            Assert.That(
                never.Elapsed,
                Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(90)),
                "an unsatisfied poll returns around the deadline, not immediately");
        });
    }

    [Test]
    public async Task PollAsync_ShouldNotSpinWhenLessThanAMillisecondRemains()
    {
        // A deadline under a millisecond away must still be waited out, not polled in a tight loop:
        // Task.Delay truncates a sub-millisecond span to zero and returns at once.
        var attempts = 0;
        var result = await ProtoPolling.PollAsync(
            _ =>
            {
                attempts++;
                return ValueTask.FromResult(false);
            },
            value => value,
            TimeSpan.FromTicks(5_000),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Satisfied, Is.False);
            Assert.That(attempts, Is.LessThanOrEqualTo(3), "one probe, the deadline probe and timer slack");
        });
    }
}
