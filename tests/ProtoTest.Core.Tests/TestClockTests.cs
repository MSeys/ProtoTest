namespace ProtoTest.Core.Tests;

[TestFixture]
public sealed class TestClockTests
{
    private static readonly DateTimeOffset Seed = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Clock_ShouldBePerTestSeededFromTheRunAndRecordedInTheTrace()
    {
        // Entries are what Enabled = false turns off; this test needs them, so it writes a temp archive.
        using var trace = new TemporaryTrace("clock");
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(Seed));
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
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
    public async Task RunClock_WhenAdvancedInParallel_ShouldSumEveryDeltaAndRecordEachAdvance()
    {
        var seed = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(seed));
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();

        const int Workers = 8;
        const int AdvancesPerWorker = 250;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            for (var index = 0; index < AdvancesPerWorker; index++)
            {
                host.Clock.Advance(TimeSpan.FromTicks(1));
            }
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(workers);

        var entries = host.Trace.Snapshot().Entries!;
        Assert.Multiple(() =>
        {
            Assert.That(
                host.Clock.GetUtcNow(),
                Is.EqualTo(seed.AddTicks((long)Workers * AdvancesPerWorker)),
                "concurrent advances add up instead of losing updates");
            Assert.That(
                entries.Count(entry => entry.Kind == "clock.advance"),
                Is.EqualTo(Workers * AdvancesPerWorker),
                "each advance records its own event");
        });

        await host.StopAsync();
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
