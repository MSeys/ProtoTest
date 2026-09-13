namespace ProtoTest.Core.Tests;

using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;

[TestFixture]
public sealed class ProtoTracingTests
{
    [Test]
    public async Task PassingTestWithFailedDiagnostic_ShouldBePartial()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("partial test", TestMethod());
        context.Trace.WriteEvent(
            "probe.failed",
            "Non-blocking probe failed",
            "ProtoTest.Core.Tests",
            outcome: ProtoTraceOutcome.Failed);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(test.Entries.Single(entry => entry.Kind == "test.execution").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(test.Entries.Single(entry => entry.Kind == "probe.failed").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Failed));
        });
    }

    [Test]
    public async Task Lifecycle_ShouldProduceParentedExecutionTrace()
    {
        var path = TemporaryTracePath();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = path;
        });
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("traced test", TestMethod());
        context.AddAttachment(ProtoTestAttachment.FromText(
            "response.json",
            "{\"status\":\"ready\"}",
            "application/json"));
        using (var operation = context.Trace.StartOperation(
                   "sample.operation", "Sample operation", "ProtoTest.Core.Tests"))
        {
            context.Trace.WriteEvent("sample.event", "Sample event", "ProtoTest.Core.Tests");
            operation.Succeed();
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var run = host.Trace.Snapshot();
        var test = run.Tests.Single();
        var execution = test.Entries.Single(entry => entry.Kind == "test.execution");
        var sampleOperation = test.Entries.Single(entry => entry.Kind == "sample.operation");
        var sampleEvent = test.Entries.Single(entry => entry.Kind == "sample.event");
        var attachmentEntry = test.Entries.Single(entry => entry.Kind == "attachment.register");

        Assert.Multiple(() =>
        {
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(sampleOperation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(sampleOperation.ParentId, Is.EqualTo(execution.Id));
            Assert.That(sampleEvent.ParentId, Is.EqualTo(sampleOperation.Id));
            Assert.That(attachmentEntry.Attributes["attachment.artifact_id"], Is.EqualTo("artifact-1"));
            Assert.That(attachmentEntry.Attributes["attachment.archive_path"], Is.Not.Empty);
            Assert.That(attachmentEntry.Attributes["attachment.size_bytes"], Is.EqualTo("18"));
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "test.setup"));
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "test.teardown"));
            Assert.That(File.Exists(path), Is.True);
        });

        using var archive = ZipFile.OpenRead(path);
        Assert.That(archive.GetEntry("manifest.json"), Is.Not.Null);
        Assert.That(archive.GetEntry("run.json"), Is.Not.Null);
        var artifact = test.Artifacts.Single();
        Assert.That(archive.GetEntry(artifact.ArchivePath), Is.Not.Null);
        await using var artifactStream = archive.GetEntry(artifact.ArchivePath)!.Open();
        using var reader = new StreamReader(artifactStream);
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("{\"status\":\"ready\"}"));
    }

    [Test]
    public async Task Observations_ShouldRemainSeparateFromTraceEntries()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("observation test", TestMethod());
        context.RecordObservation("Orders", "orders.seen", "42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(context.RecordedObservations, Has.Count.EqualTo(1));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries, Is.Empty);
        });
    }

    [Test]
    public async Task Trace_ShouldIncludeDetailedLifecycleEntriesWithoutOptIn()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = TemporaryTracePath();
        });
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StartTestAsync("normal test", TestMethod());
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var setup = entries.Single(entry => entry.Kind == "test.setup");
        var hook = entries.First(entry => entry.Kind == "hook.before");
        Assert.Multiple(() =>
        {
            Assert.That(hook.Attributes["hook.type"], Is.Not.Empty);
            Assert.That(hook.Attributes["hook.order"], Is.Not.Empty);
            Assert.That(setup.Attributes["hook.count"], Is.Not.Empty);
            Assert.That(setup.Attributes["attribute.count"], Is.EqualTo("0"));
            Assert.That(setup.Attributes["test.method"], Is.EqualTo(nameof(Placeholder)));
            Assert.That(entries, Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "context.dispose"));
        });
    }

    [Test]
    public async Task ChildEntries_ShouldInheritTheirParentLifecyclePhase()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        builder.AddTestHook<PhaseWritingHook>();
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StartTestAsync("phase inheritance", TestMethod());
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries.Single(entry => entry.Kind == "phase.before.event").Phase,
                Is.EqualTo(ProtoTracePhase.Setup));
            Assert.That(entries.Single(entry => entry.Kind == "phase.after.event").Phase,
                Is.EqualTo(ProtoTracePhase.Teardown));
        });
    }

    [Test]
    public async Task ContextValues_ShouldBeCapturedAsRedactedStructuredData()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("context trace", TestMethod());

        context.SetContext(new DiagnosticContext("orders", "do-not-record"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var entry = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "context.set");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["context.value"], Does.Contain("orders"));
            Assert.That(entry.Attributes["context.value"], Does.Contain("[REDACTED]"));
            Assert.That(entry.Attributes["context.value"], Does.Not.Contain("do-not-record"));
        });
    }

    [Test]
    public async Task MissingAttachment_ShouldNotPreventTraceExport()
    {
        var tracePath = TemporaryTracePath();
        var attachmentPath = Path.Combine(Path.GetDirectoryName(tracePath)!, "temporary.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);
        await File.WriteAllTextAsync(attachmentPath, "temporary");

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = tracePath);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("missing attachment", TestMethod());
        context.AddAttachment(ProtoTestAttachment.FromFile(attachmentPath));
        File.Delete(attachmentPath);
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.DoesNotThrowAsync(async () => await host.StopAsync());
        using var archive = ZipFile.OpenRead(tracePath);
        using var reader = new StreamReader(archive.GetEntry("run.json")!.Open());
        using var json = JsonDocument.Parse(await reader.ReadToEndAsync());
        var error = json.RootElement.GetProperty("tests")[0].GetProperty("artifacts")[0].GetProperty("error");
        Assert.That(error.GetString(), Is.Not.Empty);
    }

    [Test]
    public async Task FileAttachment_ShouldBeSnapshottedBeforeRunExport()
    {
        var tracePath = TemporaryTracePath();
        var attachmentPath = Path.Combine(Path.GetDirectoryName(tracePath)!, "temporary.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);
        await File.WriteAllTextAsync(attachmentPath, "captured before deletion");

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = tracePath);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("snapshotted attachment", TestMethod());
        context.AddAttachmentFile(attachmentPath, "temporary.txt", "text/plain");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        File.Delete(attachmentPath);
        await host.StopAsync();

        var artifact = host.Trace.Snapshot().Tests.Single().Artifacts.Single();
        using var archive = ZipFile.OpenRead(tracePath);
        await using var stream = archive.GetEntry(artifact.ArchivePath)!.Open();
        using var reader = new StreamReader(stream);
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("captured before deletion"));
    }

    [Test]
    public async Task ContextSnapshot_ShouldDescribeUnsupportedPropertiesWithoutDuplicatingValuesOnResolve()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("context diagnostics", TestMethod());
        context.SetContext(new CallbackContext("configured", () => { }));
        _ = context.Context<CallbackContext>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var set = entries.Single(entry => entry.Kind == "context.set" && entry.Attributes["context.type"]!.Contains(nameof(CallbackContext)));
        var resolved = entries.Single(entry => entry.Kind == "context.resolve" && entry.Attributes["context.type"]!.Contains(nameof(CallbackContext)));
        Assert.Multiple(() =>
        {
            Assert.That(set.Attributes["context.value"], Does.Contain("configured"));
            Assert.That(set.Attributes["context.value"], Does.Contain("delegate"));
            Assert.That(set.Attributes["context.value"], Does.Not.Contain("\"unavailable\":true"));
            Assert.That(resolved.Attributes, Does.Not.ContainKey("context.value"));
        });
    }

    [Test]
    public async Task OperationsAndEvents_ShouldFlowThroughActivitySource()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ProtoTestDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("activity trace", TestMethod());
        using (var operation = context.Trace.StartOperation("sample.activity", "Sample activity", "ProtoTest.Core.Tests"))
        {
            context.Trace.WriteEvent("sample.event", "Sample event", "ProtoTest.Core.Tests", outcome: ProtoTraceOutcome.Succeeded);
            operation.Succeed();
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var activity = stopped.Single(item => item.GetTagItem("prototest.entry.kind") as string == "sample.activity");
        Assert.Multiple(() =>
        {
            Assert.That(activity.Source.Name, Is.EqualTo(ProtoTestDiagnostics.ActivitySourceName));
            Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Ok));
            Assert.That(activity.Events.Select(item => item.Name), Does.Contain("Sample event"));
            Assert.That(activity.GetTagItem("prototest.test.id"), Is.Not.Null);
        });
    }

    private static MethodInfo TestMethod()
        => typeof(ProtoTracingTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }

    private sealed record DiagnosticContext(string Name, string Token) : IProtoContext;
    private sealed record CallbackContext(string Name, Action Callback) : IProtoContext;

    private sealed class PhaseWritingHook : IProtoTestHook
    {
        public Task BeforeTestAsync(ProtoExecutionContext context)
        {
            context.Trace.WriteEvent("phase.before.event", "Setup child", "ProtoTest.Core.Tests");
            return Task.CompletedTask;
        }

        public Task AfterTestAsync(ProtoExecutionContext context)
        {
            context.Trace.WriteEvent("phase.after.event", "Teardown child", "ProtoTest.Core.Tests");
            return Task.CompletedTask;
        }
    }

    private static string TemporaryTracePath()
        => Path.Combine(Path.GetTempPath(), "ProtoTest.Core.Tests", Guid.NewGuid().ToString("N"), "run.prototrace");
}
