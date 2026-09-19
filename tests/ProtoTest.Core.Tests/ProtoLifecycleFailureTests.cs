namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class ProtoLifecycleFailureTests
{
    [Test]
    public async Task FailedAttributeSetup_ShouldRollbackCompletedComponentsInReverseOrder()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook>(new TrackingHook("FirstHook", 10, events));
        services.AddSingleton<IProtoTestHook>(new TrackingHook("SecondHook", 20, events));
        await using var host = new ProtoHost(services.BuildServiceProvider());
        ProtoAttribute[] attributes =
        [
            new TrackingAttribute("FirstAttribute", events) { Order = 10 },
            new TrackingAttribute("FailingAttribute", events, failBefore: true) { Order = 20 }
        ];

        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartTestAsync(
            "Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!, attributes));

        Assert.That(events, Is.EqualTo(new[]
        {
            "FirstHook:Before",
            "SecondHook:Before",
            "FirstAttribute:Before",
            "FailingAttribute:Before",
            "FirstAttribute:After",
            "SecondHook:After",
            "FirstHook:After"
        }));
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    [Test]
    public async Task Teardown_ShouldAttemptEveryComponentAndAggregateMultipleFailures()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook>(new TrackingHook("FirstHook", 10, events, failAfter: true));
        services.AddSingleton<IProtoTestHook>(new TrackingHook("SecondHook", 20, events, failAfter: true));
        await using var host = new ProtoHost(services.BuildServiceProvider());
        ProtoAttribute[] attributes =
        [
            new TrackingAttribute("FirstAttribute", events, failAfter: true) { Order = 10 },
            new TrackingAttribute("SecondAttribute", events, failAfter: true) { Order = 20 }
        ];
        await host.StartTestAsync("Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!, attributes);
        events.Clear();

        var exception = Assert.ThrowsAsync<AggregateException>(async () => await host.CompleteTestAsync());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(4));
        Assert.That(events, Is.EqualTo(new[]
        {
            "SecondAttribute:After",
            "FirstAttribute:After",
            "SecondHook:After",
            "FirstHook:After"
        }));
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    [Test]
    public async Task ArtifactCaptureFailure_ShouldStillCompleteTheTestAndClearTheContext()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        context.AddAttachment(CreateUnreadableAttachment());

        // Act: a non-IO failure while capturing artifacts is a teardown failure like any other.
        var exception = Assert.ThrowsAsync<ArgumentNullException>(
            async () => await host.CompleteTestAsync(ProtoTestResult.Passed));

        // Assert: completing the test and clearing the ambient context must happen regardless.
        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Outcome,
                Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.CurrentContext);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task PartialArtifactCapture_ShouldNotLeaveDanglingReferences()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        context.AddAttachment(ProtoTestAttachment.FromText("good.txt", "content", "text/plain"));
        context.AddAttachment(CreateUnreadableAttachment());

        // Act: the second capture fails, so only the first attachment is published.
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await host.CompleteTestAsync(ProtoTestResult.Passed));

        // Assert: no attachment record may point at an artifact the snapshot does not declare.
        var test = host.Trace.Snapshot().Tests.Single();
        var declared = test.Artifacts.Select(artifact => artifact.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(test.Artifacts, Has.Count.EqualTo(1));
            Assert.That(declared, Does.Contain("artifact-1"));
            foreach (var record in test.Record!.Attachments!.Where(record => record.ArtifactId is not null))
            {
                Assert.That(declared, Does.Contain(record.ArtifactId));
            }
        });
        await host.StopAsync();
    }

    [Test]
    public async Task FailingTeardown_ShouldKeepTheOriginalFailureAndRecordTheTeardownFailure()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook>(new TrackingHook("FailingHook", 10, events, failAfter: true));
        await using var host = new ProtoHost(services.BuildServiceProvider());
        await host.StartTestAsync("Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        var assertion = new InvalidOperationException("the assertion failed");

        // Act: the original result is a failed assertion; teardown fails too.
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.CompleteTestAsync(ProtoTestResult.Failed(assertion)));

        // Assert: the teardown error reaches the caller and the trace, but it does not replace the
        // assertion the test reported.
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("FailingHook failed"));
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(test.Error!.Message, Is.EqualTo("the assertion failed"));
            Assert.That(
                test.Entries.Single(entry => entry.Kind == "test.teardown").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(
                test.Record!.Findings,
                Has.Some.Matches<ProtoTraceFindingRecord>(finding =>
                    finding.Category == "Teardown"
                    && finding.Status == ProtoReportStatus.Error.ToString()
                    && finding.Message.Contains("the assertion failed") == false
                    && finding.Message.Contains("FailingHook failed")));
        });
    }

    [Test]
    public async Task FailingTeardown_OnAPassingTest_ShouldRecordPartialWithoutAnError()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook>(new TrackingHook("FailingHook", 10, events, failAfter: true));
        await using var host = new ProtoHost(services.BuildServiceProvider());
        await host.StartTestAsync("Failure", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.CompleteTestAsync(ProtoTestResult.Passed));

        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("FailingHook failed"));
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(test.Error, Is.Null, "a teardown failure is not the test's own error");
            Assert.That(
                test.Record!.Findings,
                Has.Some.Matches<ProtoTraceFindingRecord>(finding =>
                    finding.Category == "Teardown" && finding.Message.Contains("FailingHook failed")));
        });
    }

    private static ProtoTestAttachment CreateUnreadableAttachment()
    {
        // A file-less attachment with no content: reading it throws ArgumentNullException, not IOException.
        var attachment = ProtoTestAttachment.FromText("unreadable", "content");
        typeof(ProtoTestAttachment)
            .GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(attachment, null);
        return attachment;
    }

    private sealed class TrackingHook(
        string name,
        int order,
        List<string> events,
        bool failAfter = false) : IProtoTestHook
    {
        public int Order => order;

        public Task BeforeTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:Before");
            return Task.CompletedTask;
        }

        public Task AfterTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:After");
            return failAfter
                ? Task.FromException(new InvalidOperationException($"{name} failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class TrackingAttribute(
        string name,
        List<string> events,
        bool failBefore = false,
        bool failAfter = false) : ProtoAttribute
    {
        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:Before");
            return failBefore
                ? Task.FromException(new InvalidOperationException($"{name} failed"))
                : Task.CompletedTask;
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            events.Add($"{name}:After");
            return failAfter
                ? Task.FromException(new InvalidOperationException($"{name} failed"))
                : Task.CompletedTask;
        }
    }
}
