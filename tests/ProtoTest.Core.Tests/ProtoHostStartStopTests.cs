namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

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
    public async Task StartAsync_WhenInfrastructureFails_ShouldReleaseWhatStartedAndAllowRetry()
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
            Assert.That(first.ReleaseCount, Is.EqualTo(1), "a resource is released at most once, even across a retry");
            Assert.That(second.ReleaseCount, Is.EqualTo(1));
        });
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
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

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

    private sealed class TrackingInfrastructure(string id, int failuresBeforeStart = 0) : IProtoInfrastructure
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
            return ValueTask.CompletedTask;
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
