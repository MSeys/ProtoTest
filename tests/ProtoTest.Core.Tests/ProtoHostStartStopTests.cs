namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class ProtoHostStartStopTests
{
    [Test]
    public async Task StartAsync_AfterASuccessfulStart_ShouldNotRunAnythingAgain()
    {
        var infrastructure = new TrackingInfrastructure("once");
        var hook = new CountingRunHook();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        builder.AddCapability(new ProtoCapabilityDescriptor("Stub", "protocol", "Tests"));
        builder.AddInfrastructure(infrastructure);
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StartAsync();

        var capability = host.Trace.Snapshot().Entities!
            .Single(entity => entity.Kind == ProtoTraceEntityKinds.Capability);
        Assert.Multiple(() =>
        {
            Assert.That(hook.BeforeRunCount, Is.EqualTo(1), "the run hooks run once");
            Assert.That(infrastructure.StartCount, Is.EqualTo(1), "infrastructure starts once");
            Assert.That(capability.Versions, Has.Count.EqualTo(1), "capabilities are recorded once");
        });

        await host.StopAsync();
        Assert.That(hook.AfterRunCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StopAsync_DuringStartAsync_ShouldBeRejectedAndNotTearDownTheRun()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new BlockingRunHook(entered, release);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();

        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StopAsync());
        Assert.That(exception!.Message, Does.Contain("in progress"));

        release.SetResult();
        await start;

        Assert.Multiple(() =>
        {
            Assert.That(hook.AfterRunCount, Is.EqualTo(0), "the rejected stop did not tear the run down");
            Assert.That(
                host.Trace.Snapshot().CompletedAtUtc,
                Is.Null,
                "the rejected stop did not end the run's trace");
        });

        await host.StopAsync();
        Assert.That(hook.AfterRunCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_DuringStartAsync_ShouldBeRejectedInsteadOfBeingUndone()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new BlockingRunHook(entered, release);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        var host = builder.Build();

        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
        Assert.That(exception!.Message, Does.Contain("in progress"));

        release.SetResult();
        await start;

        await host.StopAsync();
        await host.DisposeAsync();
    }

    [Test]
    public async Task StartAsync_WhenInfrastructureFails_ShouldReleaseEachOwnershipAndAllowRetry()
    {
        var first = new TrackingInfrastructure("first");
        var second = new TrackingInfrastructure("second", failuresBeforeStart: 1);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(first);
        builder.AddInfrastructure(second);
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("second failed to start."));
            Assert.That(first.StartCount, Is.EqualTo(1));
            Assert.That(first.ReleaseCount, Is.EqualTo(1), "the started infrastructure is released on failure");
            Assert.That(second.ReleaseCount, Is.EqualTo(1), "the failed infrastructure is released too");
        });

        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.StartCount, Is.EqualTo(2), "a retry starts the released infrastructure again");
            Assert.That(second.StartCount, Is.EqualTo(2));
            Assert.That(first.ReleaseCount, Is.EqualTo(2), "a restarted resource is released with its new ownership period");
            Assert.That(second.ReleaseCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StartAsync_WhenRollbackReleaseFails_ShouldReleaseTheRetriedOwnershipPeriod()
    {
        // Stage 2 (Audit 3, finding B1): a failed release is re-armed when the host starts the resource
        // again, so the retried ownership period is released too.
        var failing = new TrackingInfrastructure("failing", failuresBeforeStart: 1, failuresBeforeRelease: 1);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(failing);
        await using var host = builder.Build();

        Assert.CatchAsync(async () => await host.StartAsync());
        Assert.That(failing.ReleaseCount, Is.EqualTo(1), "the first ownership period's release failed");

        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(failing.StartCount, Is.EqualTo(2), "the retry started the resource again");
            Assert.That(
                failing.ReleaseCount,
                Is.EqualTo(2),
                "the retried ownership period is released as well");
        });
    }

    [Test]
    public async Task StartAsync_WhenInfrastructureFails_ShouldNotExportReportsOrTrace()
    {
        // Stage 2 (Audit 3, finding B4): a run that never finished starting exports nothing. Only the
        // run-resource rollback runs, so no sink is asked to export and no archive is written.
        var output = Path.Combine(Path.GetTempPath(), $"prototest-start-failure-{Guid.NewGuid():N}.prototrace");
        try
        {
            var sink = new CapturingSink();
            var builder = new ProtoHostBuilder();
            builder.AddSink(sink);
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddInfrastructure(new TrackingInfrastructure("failing", failuresBeforeStart: 1));
            await using var host = builder.Build();

            // Act
            Assert.CatchAsync(async () => await host.StartAsync());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(sink.Items, Is.Empty, "no report is exported for a run that never started");
                Assert.That(File.Exists(output), Is.False, "no archive is written for a run that never started");
            });

            // A retry runs and stops normally, so the next run does export.
            await host.StartAsync();
            await host.StopAsync();
            Assert.That(File.Exists(output), Is.True);
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
    public async Task StartAsync_WhenInfrastructureFails_ShouldUnwindCompletedUserHooks()
    {
        // A5.2 (Audit 5, A5-02): every completed hook that owns state unwinds on a failed start, while
        // gates, sinks and the archive stay silent for a run that never started (Audit 3 B4).
        var events = new List<string>();
        var first = new TrackingRunHook("First", events);
        var second = new TrackingRunHook("Second", events);
        var sink = new CountingSink();
        var output = Path.Combine(Path.GetTempPath(), $"prototest-hook-rollback-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddSink(sink);
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IProtoRunHook>(first);
                services.AddSingleton<IProtoRunHook>(second);
            });
            builder.AddInfrastructure(new TrackingInfrastructure("failing", failuresBeforeStart: 1));
            await using var host = builder.Build();

            // Act
            Assert.CatchAsync(async () => await host.StartAsync());

            // Assert: the user hooks unwound in reverse, and nothing was exported or archived.
            Assert.Multiple(() =>
            {
                Assert.That(
                    events,
                    Is.EqualTo(new[] { "First:Before", "Second:Before", "Second:After", "First:After" }),
                    "completed user hooks unwind in reverse");
                Assert.That(first.AfterRunCount, Is.EqualTo(1));
                Assert.That(second.AfterRunCount, Is.EqualTo(1));
                Assert.That(sink.ExportCount, Is.Zero, "no report is exported for a run that never started");
                Assert.That(File.Exists(output), Is.False, "no archive is written for a run that never started");
            });

            // A retry runs normally and unwinds them again at the run's real end.
            events.Clear();
            await host.StartAsync();
            await host.StopAsync();
            Assert.Multiple(() =>
            {
                Assert.That(
                    events,
                    Is.EqualTo(new[] { "First:Before", "Second:Before", "Second:After", "First:After" }),
                    "the real run end unwinds the hooks again");
                Assert.That(sink.ExportCount, Is.EqualTo(1));
                Assert.That(File.Exists(output), Is.True);
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
    public async Task StartTest_DuringStartAsync_ShouldBeRejected()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new BlockingRunHook(entered, release);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();
        var method = TestMethods.Placeholder;

        var start = host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Starting a test before the run's hooks and infrastructure finished would read half a run.
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.StartTestAsync("too early", "00001", method));
        Assert.That(exception!.Message, Does.Contain("starting"));

        release.SetResult();
        await start;

        var context = await host.StartTestAsync("on time", "00002", method);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(context, Is.Not.Null);
        await host.StopAsync();
    }

    [Test]
    public async Task DisposeAsync_DuringStopAsync_ShouldBeRejectedAndNotResurrectTheRun()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new BlockingAfterRunHook(entered, release);
        var released = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        builder.AddResource(new ProtoResource(
            "order-probe",
            "probe",
            "Counts releases",
            _ =>
            {
                released.Add("released");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));
        var host = builder.Build();

        await host.StartAsync();
        var stop = host.StopAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Disposing under an in-flight stop would let the stop write Stopped over Disposed afterwards.
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
        Assert.That(exception!.Message, Does.Contain("shutdown"));

        release.SetResult();
        await stop;
        Assert.That(hook.AfterRunCount, Is.EqualTo(1));

        // The run is still disposable once the stop has finished, and the resource was released once.
        await host.DisposeAsync();
        Assert.That(released, Is.EqualTo(new[] { "released" }));

        // A disposed host must not report a start as successful.
        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
    }

    [Test]
    public async Task StartAsync_DuringStopAsync_ShouldBeRejected()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new BlockingAfterRunHook(entered, release);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();

        await host.StartAsync();
        var stop = host.StopAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        Assert.That(exception!.Message, Does.Contain("shutdown"));

        release.SetResult();
        await stop;

        Assert.Multiple(() =>
        {
            Assert.That(hook.BeforeRunCount, Is.EqualTo(1), "the rejected start did not run the hooks again");
            Assert.That(host.Trace.Snapshot().CompletedAtUtc, Is.Not.Null, "the stop still completed the run");
        });
    }

    [Test]
    public async Task StopAsync_WhenAHookFails_ShouldRememberTheFailureOnRetry()
    {
        var hook = new FailingAfterRunHook();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();
        await host.StartAsync();

        var first = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StopAsync());
        var second = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StopAsync());

        Assert.Multiple(() =>
        {
            Assert.That(second!.Message, Is.EqualTo(first!.Message));
            Assert.That(
                hook.AfterRunCount,
                Is.EqualTo(1),
                "a failed stop is remembered rather than silently reported as a success or re-run");
        });
    }

    private sealed class TrackingInfrastructure(
        string id,
        int failuresBeforeStart = 0,
        int failuresBeforeRelease = 0) : IProtoInfrastructure
    {
        public string Id { get; } = id;
        public string Kind => "tracked";
        public string Description { get; } = $"Tracked infrastructure {id}";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;
        public int StartCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return StartCount <= failuresBeforeStart
                ? ValueTask.FromException(new InvalidOperationException($"{Id} failed to start."))
                : ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
        {
            ReleaseCount++;
            return ReleaseCount <= failuresBeforeRelease
                ? ValueTask.FromException(new InvalidOperationException($"Release of {Id} failed."))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class CountingRunHook : IProtoRunHook
    {
        public int BeforeRunCount { get; private set; }
        public int AfterRunCount { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            BeforeRunCount++;
            return Task.CompletedTask;
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            AfterRunCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingRunHook(string name, List<string> events) : IProtoRunHook
    {
        public int AfterRunCount { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            events.Add($"{name}:Before");
            return Task.CompletedTask;
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            AfterRunCount++;
            events.Add($"{name}:After");
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingRunHook(
        TaskCompletionSource entered,
        TaskCompletionSource release) : IProtoRunHook
    {
        public int AfterRunCount { get; private set; }

        public async Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            AfterRunCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingAfterRunHook(
        TaskCompletionSource entered,
        TaskCompletionSource release) : IProtoRunHook
    {
        public int BeforeRunCount { get; private set; }
        public int AfterRunCount { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            BeforeRunCount++;
            return Task.CompletedTask;
        }

        public async Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            AfterRunCount++;
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailingAfterRunHook : IProtoRunHook
    {
        public int AfterRunCount { get; private set; }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            AfterRunCount++;
            return Task.FromException(new InvalidOperationException("The after-run hook failed."));
        }
    }
}
