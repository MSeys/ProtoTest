namespace ProtoTest.Core.Tests;

[TestFixture]
public sealed class ProtoClockTests
{
    [Test]
    public void ProtoClock_ShouldStartAtItsSeedAndAdvance()
    {
        var seed = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ProtoClock(seed);

        Assert.That(clock.GetUtcNow(), Is.EqualTo(seed));

        clock.Advance(TimeSpan.FromHours(5));

        Assert.That(clock.GetUtcNow(), Is.EqualTo(seed.AddHours(5)));
    }

    [Test]
    public void ProtoClock_ShouldRejectBackwardsAdvanceAndAllowSet()
    {
        var seed = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ProtoClock(seed);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromMinutes(-1)));

        clock.SetUtcNow(seed.AddDays(-1));
        Assert.That(clock.GetUtcNow(), Is.EqualTo(seed.AddDays(-1)));
    }

    [Test]
    [Category("Characterization")]
    public async Task ProtoRequestClock_Push_ShouldBeCapturedByADetachedTaskNotRevoked()
    {
        // Documented limit (audit CFG-4): a push is restored on scope disposal, not revoked. A task
        // started inside the scope captured the pushed clock in its own execution context, so it keeps
        // the finished test's clock; long-lived background work must read the run clock instead.
        var seed = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ProtoClock(seed);
        Task<ProtoClock?> detached;
        using (ProtoRequestClock.Push(clock))
        {
            detached = Task.Run(() => ProtoRequestClock.Current);
        }

        var captured = await detached;
        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.SameAs(clock), "the detached task captured the pushed clock");
            Assert.That(ProtoRequestClock.Current, Is.Null, "the scope restored the ambient clock");
        });
    }
}
