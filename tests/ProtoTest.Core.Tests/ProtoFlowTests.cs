namespace ProtoTest.Core.Tests;

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
    public async Task RunAsync_InCollectMode_ShouldCollectACancelledStepAndRunTheRest()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow collect cancelled", TestMethods.Placeholder);

        var ran = new List<string>();
        using var stepCancellation = new CancellationTokenSource();
        await stepCancellation.CancelAsync();
        var result = await new ProtoFlow("teardown", Source, ProtoFlowFailureMode.Collect)
            .Step("cancelled", _ => { ran.Add("cancelled"); return ValueTask.FromCanceled(stepCancellation.Token); })
            .Step("after", _ => { ran.Add("after"); return ValueTask.CompletedTask; })
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Failed(result.Failures[0]));
        Assert.Multiple(() =>
        {
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0], Is.InstanceOf<OperationCanceledException>());
            Assert.That(ran, Is.EqualTo(new[] { "cancelled", "after" }));
        });
    }

    [Test]
    public async Task RunAsync_WithADescriptor_ShouldRecordTheDeclaredOperationAndEntity()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("flow descriptor", TestMethods.Placeholder);

        var result = await new ProtoFlow("resource.release", Source, ProtoFlowFailureMode.Collect)
            .Step(
                new ProtoStepDescriptor(
                    "resource.release",
                    "Release · database:connection",
                    Source,
                    Attributes: new Dictionary<string, string?> { ["resource.kind"] = "database" },
                    EntityKind: "database",
                    EntityId: "database:connection"),
                _ => ValueTask.CompletedTask)
            .RunAsync(context.Trace);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var step = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "resource.release");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(step.Name, Is.EqualTo("Release · database:connection"));
            Assert.That(step.EntityKind, Is.EqualTo("database"));
            Assert.That(step.EntityId, Is.EqualTo("database:connection"));
            Assert.That(step.Attributes["resource.kind"], Is.EqualTo("database"));
            Assert.That(step.Attributes["flow.name"], Is.EqualTo("resource.release"));
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
