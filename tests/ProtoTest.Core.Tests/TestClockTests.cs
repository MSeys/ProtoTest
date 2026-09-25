namespace ProtoTest.Core.Tests;

[TestFixture]
public sealed class TestClockTests
{
    private static readonly DateTimeOffset Seed = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Clock_ShouldBePerTestSeededFromTheRunAndRecordedInTheTrace()
    {
        // Entries are what Enabled = false turns off; this test needs them, so it writes a temp archive.
        var output = Path.Combine(Path.GetTempPath(), $"prototest-clock-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureClock(new ProtoClock(Seed));
            builder.ConfigureTracing(options => options.OutputPath = output);
            await using var host = builder.Build();
            await host.StartAsync();

            await host.StartTestAsync("first", "00001", TestMethods.Placeholder);
            var first = Proto.Context.Clock;
            Assert.That(first.GetUtcNow(), Is.EqualTo(Seed), "a test clock starts where the run clock points");

            first.Advance(TimeSpan.FromHours(5));
            await host.CompleteTestAsync(ProtoTestResult.Passed);

            await host.StartTestAsync("second", "00002", TestMethods.Placeholder);
            var second = Proto.Context.Clock;
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            var snapshot = host.Trace.Snapshot();
            var firstTrace = snapshot.Tests.Single(test => test.Name == "first");
            var advance = firstTrace.Entries.Single(entry => entry.Kind == "clock.advance");
            var entity = firstTrace.Entities!.Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Clock);
            Assert.Multiple(() =>
            {
                Assert.That(second.GetUtcNow(), Is.EqualTo(Seed), "the next test starts from the seed, not the previous test's advances");
                Assert.That(first.GetUtcNow(), Is.EqualTo(Seed.AddHours(5)));
                Assert.That(advance.Attributes["clock.delta"], Is.EqualTo("5:00:00"));
                Assert.That(advance.EntityKind, Is.EqualTo(ProtoTraceEntityKinds.Clock));
                Assert.That(entity.State["clock.utcNow"], Is.EqualTo(Seed.AddHours(5).ToString("O")));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task RunClock_ShouldSeedLaterTestsAndRecordOnTheRun()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(Seed));
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();

        host.Clock.Advance(TimeSpan.FromHours(2));

        await host.StartTestAsync("after run advance", "00001", TestMethods.Placeholder);
        var clock = Proto.Context.Clock;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(clock.GetUtcNow(), Is.EqualTo(Seed.AddHours(2)));
            Assert.That(
                snapshot.Entries!.Any(entry => entry.Kind == "clock.advance"),
                Is.True,
                "advancing the run clock is recorded on the run");
        });
    }

    [Test]
    public async Task Clock_WithoutASeed_ShouldStartAtRealTime()
    {
        var before = DateTimeOffset.UtcNow;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("unseeded", "00001", TestMethods.Placeholder);

        var clock = Proto.Context.Clock;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(clock.GetUtcNow(), Is.InRange(before, DateTimeOffset.UtcNow));
    }
}
