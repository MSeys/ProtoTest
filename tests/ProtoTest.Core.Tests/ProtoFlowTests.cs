namespace ProtoTest.Core.Tests;

using NUnit.Framework;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoFlowTests
{
    private const string Source = "ProtoTest.Core.Tests";

    [Test]
    public async Task RunAsync_ShouldRunNamedStepsInOrderAndTraceEachStep()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow order", TestMethods.Placeholder);

        var ran = new List<string>();
        var result = await new ProtoFlow("checkout", Source)
            .Step("first", _ => { ran.Add("first"); return ValueTask.CompletedTask; })
            .Step("second", _ => { ran.Add("second"); return ValueTask.CompletedTask; })
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var steps = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "flow.step")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(ran, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(steps, Has.Length.EqualTo(2));
            Assert.That(steps.Select(step => step.Attributes["step.name"]), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(steps.Select(step => step.Attributes["step.index"]), Is.EqualTo(new[] { "0", "1" }));
            Assert.That(steps.Select(step => step.Attributes["flow.name"]), Is.EqualTo(new[] { "checkout", "checkout" }));
            Assert.That(steps.Select(step => step.Outcome), Is.All.EqualTo(ProtoTraceOutcome.Succeeded));
        });
    }

    [Test]
    public async Task RunAsync_WhenAStepFails_ShouldStopAtTheFirstFailure()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow fail fast", TestMethods.Placeholder);

        var ran = new List<string>();
        var result = await new ProtoFlow("failing", Source)
            .Step("ok", _ => { ran.Add("ok"); return ValueTask.CompletedTask; })
            .Step("boom", _ => throw new InvalidOperationException("boom"))
            .Step("never", _ => { ran.Add("never"); return ValueTask.CompletedTask; })
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Failed(result.Failures[0]));
        var steps = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "flow.step")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0], Is.InstanceOf<InvalidOperationException>());
            Assert.That(ran, Is.EqualTo(new[] { "ok" }));
            Assert.That(steps, Has.Length.EqualTo(2), "the flow stops at the failure");
            Assert.That(steps[1].Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        });
    }

    [Test]
    public async Task RunAsync_InCollectMode_ShouldRunEveryStepAndReportEveryFailure()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow collect", TestMethods.Placeholder);

        var ran = new List<string>();
        var result = await new ProtoFlow("teardown", Source, ProtoFlowFailureMode.Collect)
            .Step("first", _ => { ran.Add("first"); throw new InvalidOperationException("first"); })
            .Step("second", _ => { ran.Add("second"); return ValueTask.CompletedTask; })
            .Step("third", _ => { ran.Add("third"); throw new InvalidOperationException("third"); })
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Failed(result.Failures[0]));
        Assert.Multiple(() =>
        {
            Assert.That(result.Failures, Has.Count.EqualTo(2));
            Assert.That(ran, Is.EqualTo(new[] { "first", "second", "third" }));
        });
    }

    [Test]
    public async Task RunAsync_ShouldRetryAFailedStepUntilItSucceeds()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow retry", TestMethods.Placeholder);

        var attempts = 0;
        var result = await new ProtoFlow("retrying", Source)
            .Step(
                "flaky",
                _ =>
                {
                    attempts++;
                    return attempts < 3
                        ? throw new InvalidOperationException("transient")
                        : ValueTask.CompletedTask;
                },
                new ProtoStepOptions { RetryCount = 3, RetryDelay = TimeSpan.Zero })
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var step = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "flow.step");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(step.Attributes["step.attempts"], Is.EqualTo("3"));
        });
    }

    [Test]
    public async Task RunAsync_ShouldTimeOutAStep()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow timeout", TestMethods.Placeholder);

        var result = await new ProtoFlow("timing out", Source)
            .Step(
                "slow",
                async token => await Task.Delay(TimeSpan.FromSeconds(5), token),
                new ProtoStepOptions { Timeout = TimeSpan.FromMilliseconds(50) })
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Failed(result.Failures[0]));
        Assert.Multiple(() =>
        {
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0], Is.InstanceOf<TimeoutException>());
        });
    }

    [Test]
    public async Task RunAsync_WhenCancelled_ShouldStopAndPropagate()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow cancelled", TestMethods.Placeholder);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var flow = new ProtoFlow("cancelled", Source)
            .Step("never", _ => ValueTask.CompletedTask);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await flow.RunAsync(context.Trace, cancellation.Token));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }
}
