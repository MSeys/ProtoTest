namespace ProtoTest.Core.Tests;

/// <summary>
/// Pins <see cref="ProtoClockLocator"/> as a process-global map keyed by test id alone (audit CFG-3):
/// a later registration overwrites an earlier one and removal is unscoped, so two hosts that share a
/// test id cannot each keep their own clock.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class ClockLocatorTests
{
    [Test]
    public void ClockLocator_ForTheSameTestId_ShouldKeepTheLastRegistration()
    {
        var testId = $"cfg3-{Guid.NewGuid():N}";
        var first = new ProtoClock();
        var second = new ProtoClock();
        try
        {
            ProtoClockLocator.Add(testId, first);
            ProtoClockLocator.Add(testId, second);

            // Pins current behavior; audit CFG-3 keys the locator by host identity plus test id.
            Assert.That(
                ProtoClockLocator.Find(testId),
                Is.SameAs(second),
                "a second host's clock for the same test id overwrites the first host's");
        }
        finally
        {
            ProtoClockLocator.Remove(testId);
        }
    }

    [Test]
    public void ClockLocator_RemoveByTestId_ShouldBeUnscoped()
    {
        var testId = $"cfg3-{Guid.NewGuid():N}";
        var otherId = $"cfg3-{Guid.NewGuid():N}";
        var first = new ProtoClock();
        var second = new ProtoClock();
        try
        {
            ProtoClockLocator.Add(testId, first);
            ProtoClockLocator.Add(testId, second);
            ProtoClockLocator.Add(otherId, first);

            ProtoClockLocator.Remove(testId);

            Assert.Multiple(() =>
            {
                // Pins current behavior; audit CFG-3 scopes removal so a host cannot remove another's entry.
                Assert.That(
                    ProtoClockLocator.Find(testId),
                    Is.Null,
                    "removal is by test id only, so it clears whichever host registered last");
                Assert.That(
                    ProtoClockLocator.Find(otherId),
                    Is.SameAs(first),
                    "a different test id is unaffected");
            });
        }
        finally
        {
            ProtoClockLocator.Remove(testId);
            ProtoClockLocator.Remove(otherId);
        }
    }
}
