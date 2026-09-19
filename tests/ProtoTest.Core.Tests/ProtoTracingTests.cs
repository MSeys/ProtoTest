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
    public async Task Operation_ShouldRecordWhereTheSuiteStartedIt_AndEmbedThatSource()
    {
        var path = TemporaryTracePath();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = path);
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("located test", TestMethod());
        using (var operation = context.Trace.Operation("sample.operation", "Sample", "ProtoTest.Core.Tests").Begin())
        {
            operation.Succeed();
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var test = host.Trace.Snapshot().Tests.Single();
        var located = test.Entries.Single(entry => entry.Kind == "sample.operation").Attributes;
        using var archive = ZipFile.OpenRead(path);
        using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
        var sources = manifest.RootElement.GetProperty("sources");
        var recordedPath = located["code.file.path"]!;

        Assert.Multiple(() =>
        {
            // Relative to the repository root, the same on every machine.
            Assert.That(recordedPath, Is.EqualTo("tests/ProtoTest.Core.Tests/ProtoTracingTests.cs"));
            Assert.That(int.Parse(located["code.line.number"]!), Is.GreaterThan(0));
            Assert.That(located["code.function.name"],
                Is.EqualTo("ProtoTest.Core.Tests.ProtoTracingTests.Operation_ShouldRecordWhereTheSuiteStartedIt_AndEmbedThatSource"));
            Assert.That(archive.GetEntry(sources.GetProperty(recordedPath).GetString()!), Is.Not.Null);
        });
    }

    [Test]
    public async Task SourceLocations_CanBeTurnedOff()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = TemporaryTracePath();
            options.CaptureSourceLocations = false;
        });
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("unlocated test", TestMethod());
        using (context.Trace.Operation("sample.operation", "Sample", "ProtoTest.Core.Tests").Begin())
        {
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var operation = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "sample.operation");
        Assert.That(operation.Attributes.ContainsKey("code.file.path"), Is.False);
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
        using (var operation = context.Trace
                   .Operation("sample.operation", "Sample operation", "ProtoTest.Core.Tests")
                   .Begin())
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
        var attachmentEntry = test.Record!.Attachments!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(sampleOperation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(sampleOperation.ParentId, Is.EqualTo(execution.Id));
            Assert.That(sampleEvent.ParentId, Is.EqualTo(sampleOperation.Id));
            Assert.That(attachmentEntry.ArtifactId, Is.EqualTo("artifact-1"));
            Assert.That(attachmentEntry.ArchivePath, Is.Not.Empty);
            Assert.That(attachmentEntry.SizeBytes, Is.EqualTo(18));
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "test.setup"));
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "test.teardown"));
            Assert.That(File.Exists(path), Is.True);
        });

        using var archive = ZipFile.OpenRead(path);
        Assert.That(archive.GetEntry("manifest.json"), Is.Not.Null);
        Assert.That(archive.GetEntry("spans.json"), Is.Not.Null);
        Assert.That(archive.GetEntry("state.json"), Is.Not.Null);
        Assert.That(archive.GetEntry("run.json"), Is.Null, "the v2 archive has no compatibility view");
        var artifact = test.Artifacts.Single();
        Assert.That(archive.GetEntry(artifact.ArchivePath), Is.Not.Null);
        await using var artifactStream = archive.GetEntry(artifact.ArchivePath)!.Open();
        using var reader = new StreamReader(artifactStream);
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("{\"status\":\"ready\"}"));

        // spans.json carries everything the old run.json did: each artifact once on its resource group, attachment
        // events that refer to it, and the run's timing and environment as resource attributes.
        using var spansReader = new StreamReader(archive.GetEntry("spans.json")!.Open());
        using var spans = JsonDocument.Parse(await spansReader.ReadToEndAsync());
        var groups = spans.RootElement.GetProperty("resourceSpans").EnumerateArray().ToArray();
        var testGroup = groups.Single(group => group.GetProperty("resource").GetProperty("attributes").TryGetProperty("testId", out _));
        var runAttributes = groups.Single(group => group.GetProperty("resource").GetProperty("attributes").TryGetProperty("runId", out _))
            .GetProperty("resource").GetProperty("attributes");
        var declared = testGroup.GetProperty("artifacts").EnumerateArray().Single();
        // Added before any operation began, the attachment is an orphan event of the scope rather than a span's.
        var scope = testGroup.GetProperty("scopeSpans")[0];
        var attachmentEvent = scope.GetProperty("spans").EnumerateArray()
            .SelectMany(span => span.GetProperty("events").EnumerateArray())
            .Concat(scope.GetProperty("events").ValueKind == JsonValueKind.Array ? scope.GetProperty("events").EnumerateArray() : [])
            .Single(item => item.TryGetProperty("record", out var record) && record.GetString() == "attachment");

        Assert.Multiple(() =>
        {
            Assert.That(declared.GetProperty("id").GetString(), Is.EqualTo("artifact-1"));
            Assert.That(declared.GetProperty("mediaType").GetString(), Is.EqualTo("application/json"));
            Assert.That(declared.GetProperty("archivePath").GetString(), Is.EqualTo(artifact.ArchivePath));
            Assert.That(declared.GetProperty("sizeBytes").GetInt64(), Is.EqualTo(18));
            Assert.That(attachmentEvent.GetProperty("artifactId").GetString(), Is.EqualTo("artifact-1"));
            Assert.That(attachmentEvent.TryGetProperty("mediaType", out _), Is.False, "declared once, on the artifact");
            Assert.That(runAttributes.GetProperty("runCompletedAtUtc").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(runAttributes.GetProperty("environment.runtime").GetString(), Is.Not.Empty);
            Assert.That(runAttributes.GetProperty("environment.os").GetString(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task Observations_ShouldRemainSeparateFromTraceEntries()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("observation test", TestMethod());
        context.RecordObservation("Orders", "orders.seen", "42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(context.RecordedObservations, Has.Count.EqualTo(1));
            var observation = test.Record!.Observations!.Single();
            Assert.That(observation.TargetName, Is.EqualTo("Orders"));
            Assert.That(observation.Identifier, Is.EqualTo("42"));
            Assert.That(
                test.Entries,
                Has.None.Matches<ProtoTraceEntry>(entry => entry.Kind == "observation"),
                "an observation is a record, not a timeline entry");
        });
    }

    [Test]
    public async Task Events_ShouldCoalesceOnlyWhenTheirNameAndSourceMatch()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("coalescing", TestMethod());

        var attributes = new Dictionary<string, string?> { ["probe.key"] = "same" };
        context.Trace.WriteEvent(
            "probe", "A", "ProtoTest.Core.Tests", outcome: ProtoTraceOutcome.Succeeded, attributes: attributes);
        context.Trace.WriteEvent(
            "probe", "B", "ProtoTest.Core.Tests", outcome: ProtoTraceOutcome.Succeeded, attributes: attributes);
        context.Trace.WriteEvent(
            "probe", "A", "ProtoTest.Core.Tests", outcome: ProtoTraceOutcome.Succeeded, attributes: attributes);

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var rows = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "probe")
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.Name), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(rows[0].Count, Is.EqualTo(2), "the repeated event still coalesces");
            Assert.That(rows[1].Count, Is.EqualTo(1), "a differently named event is its own row");
        });
    }

    [Test]
    public async Task EntityAndValueVersions_ShouldLinkToTheExecutionOperation()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("operation link", TestMethod());

        context.Trace.SetEntityState("entity", "order:1", "Order", change: "created");
        context.Trace.Value("value", "order:1", "Order 1", "created");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var test = host.Trace.Snapshot().Tests.Single();
        var executionId = test.Entries.Single(entry => entry.Kind == "test.execution").Id;
        Assert.Multiple(() =>
        {
            Assert.That(test.Entities!.Single().Versions!.Single().OperationId, Is.EqualTo(executionId));
            Assert.That(test.Values!.Single().Versions!.Single().OperationId, Is.EqualTo(executionId));
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
        var entity = host.Trace.Snapshot().Tests.Single().Entities!.Single(item =>
            item.Kind == ProtoTraceEntityKinds.Context && item.Id.Contains(nameof(DiagnosticContext)));
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["context.value"], Does.Contain("orders"));
            Assert.That(entity.State["context.value"], Does.Contain("[REDACTED]"));
            Assert.That(entity.State["context.value"], Does.Not.Contain("do-not-record"));
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
        using var spansReader = new StreamReader(archive.GetEntry("spans.json")!.Open());
        using var spans = JsonDocument.Parse(await spansReader.ReadToEndAsync());
        var declared = spans.RootElement.GetProperty("resourceSpans")[0].GetProperty("artifacts")[0];
        Assert.That(declared.GetProperty("error").GetString(), Is.Not.Empty);
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
        _ = context.Resolve<CallbackContext>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entity = host.Trace.Snapshot().Tests.Single().Entities!.Single(item =>
            item.Kind == ProtoTraceEntityKinds.Context && item.Id.Contains(nameof(CallbackContext)));
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["context.value"], Does.Contain("configured"));
            Assert.That(entity.State["context.value"], Does.Contain("delegate"));
            Assert.That(entity.State["context.value"], Does.Not.Contain("\"unavailable\":true"));
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
        using (var operation = context.Trace.Operation("sample.activity", "Sample activity", "ProtoTest.Core.Tests").Begin())
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

    [Test]
    public async Task ApplicationActivities_ShouldBeRoutedToTheirTestByTraceContext()
    {
        var testContext = new TaskCompletionSource<ActivityContext>();
        using var contextListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ProtoTestDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (activity.DisplayName == "Test execution")
                {
                    testContext.TrySetResult(activity.Context);
                }
            }
        };
        ActivitySource.AddActivityListener(contextListener);

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = TemporaryTracePath();
            options.ActivitySources.Add("ProtoTest.Core.Tests.Dummy");
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("application trace context", TestMethod());

        var parent = await testContext.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var applicationSource = new ActivitySource("ProtoTest.Core.Tests.Dummy");
        using (var applicationSpan = applicationSource.StartActivity(
            "invoice.pay",
            ActivityKind.Server,
            parent))
        {
            applicationSpan?.SetTag("invoice.number", "INV-1");
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var run = host.Trace.Snapshot();
        var entry = run.Tests.Single().Entries.Single(item => item.Kind == "ProtoTest.Core.Tests.Dummy");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Name, Is.EqualTo("invoice.pay"));
            Assert.That(entry.Attributes["invoice.number"], Is.EqualTo("INV-1"));
            Assert.That(run.Entries!.Any(item => item.Kind == "ProtoTest.Core.Tests.Dummy"), Is.False,
                "An application span must not leak into the run-level trace when its trace context is known.");
        });
    }

    [Test]
    public async Task ExecuteAsync_WithOperationHandle_ShouldExposeLiveAttributes()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("handle trace", TestMethod());

        await context.Trace.ExecuteAsync(
            "sample.handle",
            "Handle operation",
            "ProtoTest.Core.Tests",
            operation =>
            {
                operation.SetAttribute("handle.phase", "live");
                return ValueTask.CompletedTask;
            });

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entry = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "sample.handle");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(entry.Attributes["handle.phase"], Is.EqualTo("live"));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithOperationHandle_ShouldMarkFailedAndRethrow()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("handle failure trace", TestMethod());

        Func<ProtoTraceOperation, ValueTask> fail = _ => throw new InvalidOperationException("boom");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Trace.ExecuteAsync("sample.handle.failure", "Failing operation", "ProtoTest.Core.Tests", fail));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        var entry = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "sample.handle.failure");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(entry.Error!.Message, Is.EqualTo("boom"));
        });
    }

    [Test]
    public async Task FluentOperation_ShouldComposeMetadataAndReturnResult()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = TemporaryTracePath());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("fluent trace", TestMethod());

        var result = await context.Trace
            .Operation("sample.fluent", "Fluent operation", "ProtoTest.Core.Tests")
            .With("sample.key", "sample-value")
            .RunAsync(() => ValueTask.FromResult(42));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var test = host.Trace.Snapshot().Tests.Single();
        var entry = test.Entries.Single(item => item.Kind == "sample.fluent");
        var execution = test.Entries.Single(item => item.Kind == "test.execution");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(42));
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(entry.ParentId, Is.EqualTo(execution.Id));
            Assert.That(entry.Attributes["sample.key"], Is.EqualTo("sample-value"));
        });
    }

    [Test]
    public void Events_ShouldNotCoalesceAcrossPhases()
    {
        var recorder = new ProtoTestTraceRecorder(
            "00001", "phase coalescing", TestMethod(), new ProtoTraceOptions { Enabled = true });

        // Same name, source and (absent) parent; only the resolved phase differs, so they are two facts.
        recorder.WriteEvent("probe.phase", "Phase probe", "ProtoTest.Core.Tests", phase: ProtoTracePhase.Setup);
        recorder.WriteEvent("probe.phase", "Phase probe", "ProtoTest.Core.Tests", phase: ProtoTracePhase.Teardown);
        recorder.CompleteTest(ProtoTestResult.Passed);

        var rows = recorder.Snapshot().Entries
            .Where(entry => entry.Kind == "probe.phase")
            .OrderBy(entry => entry.Phase)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0].Phase, Is.EqualTo(ProtoTracePhase.Setup));
            Assert.That(rows[1].Phase, Is.EqualTo(ProtoTracePhase.Teardown));
            Assert.That(rows.Select(row => row.Count), Is.EqualTo(new[] { 1, 1 }));
        });
    }

    [Test]
    public async Task RunArtifacts_ShouldReceiveUniqueIdsUnderConcurrentCapture()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        var session = (ProtoTraceSession)host.Trace;
        var attachments = Enumerable.Range(1, 4)
            .Select(_ => ProtoTestAttachment.FromText("capture.txt", "content"))
            .ToArray();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var index = 0; index < 50; index++)
            {
                await session.CaptureRunArtifactsAsync(attachments, "probe", CancellationToken.None);
            }
        })));

        var ids = session.SnapshotArtifactSources().Select(source => source.Artifact.Id).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Length.EqualTo(1600));
            Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
        });
    }

    [Test]
    public async Task TimelineEvents_ShouldCarryTheirEntityReference()
    {
        var path = TemporaryTracePath();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = path);
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("entity event", TestMethod());
        using (var operation = context.Trace.Operation("sample.parent", "Parent", "ProtoTest.Core.Tests").Begin())
        {
            context.Trace.WriteEvent(
                "entity.ping",
                "Ping",
                "ProtoTest.Core.Tests",
                outcome: ProtoTraceOutcome.Succeeded,
                entityKind: "order",
                entityId: "order:1");
            operation.Succeed();
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("spans.json")!.Open());
        using var spans = JsonDocument.Parse(await reader.ReadToEndAsync());
        var timelineEvent = spans.RootElement.GetProperty("resourceSpans").EnumerateArray()
            .SelectMany(group => group.GetProperty("scopeSpans").EnumerateArray())
            .SelectMany(scope => scope.GetProperty("spans").EnumerateArray())
            .SelectMany(span => span.GetProperty("events").EnumerateArray())
            .Single(item => item.TryGetProperty("kind", out var kind) && kind.GetString() == "entity.ping");

        Assert.Multiple(() =>
        {
            Assert.That(timelineEvent.GetProperty("entityKind").GetString(), Is.EqualTo("order"));
            Assert.That(timelineEvent.GetProperty("entityId").GetString(), Is.EqualTo("order:1"));
        });
    }

    [Test]
    public async Task StateDocument_ShouldCarryTheCurrentFormatVersion()
    {
        var path = TemporaryTracePath();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = path);
        await using var host = builder.Build();

        await host.StartAsync();
        var context = await host.StartTestAsync("state version", TestMethod());
        context.Trace.Value("value", "invoice:1", "Invoice 1", "created");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("state.json")!.Open());
        using var state = JsonDocument.Parse(await reader.ReadToEndAsync());
        Assert.That(state.RootElement.GetProperty("formatVersion").GetString(), Is.EqualTo("1.1"));
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
