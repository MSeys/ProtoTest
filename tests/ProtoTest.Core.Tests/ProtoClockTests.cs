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
}
