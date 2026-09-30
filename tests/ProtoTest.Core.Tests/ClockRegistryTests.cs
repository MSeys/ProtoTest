namespace ProtoTest.Core.Tests;

/// <summary>
/// Pins the host-scoped clock lookup: one <see cref="ProtoClockRegistry"/> belongs to
/// each host, so two hosts that share a test id resolve their own clocks, a failed test start leaves
/// no entry, and host disposal clears the registry.
/// </summary>
[TestFixture]
public sealed class ClockRegistryTests
{
    private const long SharedRunPrefix = 424242;

    [Test]
    public async Task ClockRegistry_TwoHostsSharingATestId_ShouldResolveEachHostsClock()
    {
        await using var first = BuildHost();
        await using var second = BuildHost();
        await first.StartAsync();
        await second.StartAsync();

        var firstStarted = new TaskCompletionSource<ProtoExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<ProtoExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each host's test runs on its own async flow: one flow can only carry one active test, and
        // this is the shape that makes the two registrations collide.
        var firstRun = Task.Run(() => RunTestAsync(first, "first", firstStarted, releaseFirst));
        var secondRun = Task.Run(() => RunTestAsync(second, "second", secondStarted, releaseSecond));

        var firstContext = await firstStarted.Task;
        var secondContext = await secondStarted.Task;
        Assert.That(
            firstContext.TestId,
            Is.EqualTo(secondContext.TestId),
            "the hosts share the configured run prefix, so both tests have the same id");

        Assert.Multiple(() =>
        {
            Assert.That(first.FindClock(firstContext.TestId), Is.SameAs(firstContext.Clock));
            Assert.That(second.FindClock(secondContext.TestId), Is.SameAs(secondContext.Clock));
            Assert.That(
                first.FindClock(firstContext.TestId),
                Is.Not.SameAs(second.FindClock(secondContext.TestId)),
                "the same test id resolves to each host's own clock");
        });

        releaseFirst.SetResult();
        await firstRun;

        Assert.Multiple(() =>
        {
            Assert.That(
                first.FindClock(firstContext.TestId),
                Is.Null,
                "a finished test leaves its own host's registry");
            Assert.That(
                second.FindClock(secondContext.TestId),
                Is.SameAs(secondContext.Clock),
                "completing one host's test does not remove the other host's clock");
        });

        releaseSecond.SetResult();
        await secondRun;
    }

    [Test]
    public async Task ClockRegistry_WhenATestStartFails_ShouldLeaveNoClock()
    {
        await using var host = BuildHost();
        await host.StartAsync();

        Assert.Throws<InvalidOperationException>(() =>
            host.StartTestAsync("failing", "00009", TestMethods.Placeholder, new ThrowingAttributes()));

        Assert.That(host.FindClock("00009"), Is.Null, "a failed start removes its own registration");

        await host.StopAsync();
    }

    [Test]
    public async Task ClockRegistry_WhenADuplicateTestIdFailsToStart_ShouldKeepTheRunningTestsClock()
    {
        await using var host = BuildHost();
        await host.StartAsync();

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<ProtoExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Task.Run(async () =>
        {
            var context = await host.StartTestAsync("running", "00009", TestMethods.Placeholder);
            started.SetResult(context);
            await release.Task;
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        });

        var context = await started.Task;
        Assert.Throws<InvalidOperationException>(() =>
            host.StartTestAsync("duplicate", "00009", TestMethods.Placeholder));
        Assert.That(
            host.FindClock("00009"),
            Is.SameAs(context.Clock),
            "a start that never registered must not remove the running test's clock");

        release.SetResult();
        await running;
        await host.StopAsync();
    }

    [Test]
    public async Task ClockRegistry_WhenTheHostIsDisposed_ShouldClearItsClocks()
    {
        var host = BuildHost();
        await host.StartAsync();
        var context = await host.StartTestAsync("active", TestMethods.Placeholder);
        Assert.That(host.FindClock(context.TestId), Is.SameAs(context.Clock), "the test is registered while it runs");

        await host.DisposeAsync();

        Assert.That(host.FindClock(context.TestId), Is.Null, "host disposal clears the registry");
    }

    private static ProtoHost BuildHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureTestIds(options => options.RunPrefix = SharedRunPrefix);
        return builder.Build();
    }

    private static async Task RunTestAsync(
        ProtoHost host,
        string name,
        TaskCompletionSource<ProtoExecutionContext> started,
        TaskCompletionSource release)
    {
        var context = await host.StartTestAsync(name, TestMethods.Placeholder);
        started.SetResult(context);
        await release.Task;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    /// <summary>
    /// An attribute list that fails after the clock registration, to prove a failed start removes the
    /// entry it added instead of leaking it for the host's lifetime.
    /// </summary>
    private sealed class ThrowingAttributes : IEnumerable<ProtoAttribute>
    {
        public IEnumerator<ProtoAttribute> GetEnumerator() =>
            throw new InvalidOperationException("The attribute list cannot be read.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
