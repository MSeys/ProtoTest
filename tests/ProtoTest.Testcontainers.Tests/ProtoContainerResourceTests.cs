namespace ProtoTest.Testcontainers.Tests;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Testcontainers;

[TestFixture]
public sealed class ProtoContainerResourceTests
{
    [Test]
    public async Task StartAsync_ShouldBeIdempotent()
    {
        var started = 0;
        var container = new FakeContainer();
        var resource = new FakeResource(() =>
        {
            started++;
            return container;
        });

        await resource.StartAsync();
        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.EqualTo(1));
            Assert.That(container.StartCount, Is.EqualTo(1));
            Assert.That(resource.IsStarted, Is.True);
        });
    }

    [Test]
    public async Task ConnectionString_ShouldBeEmptyBeforeStartAndSetAfter()
    {
        var container = new FakeContainer { ConnectionString = "fake://host:1234" };
        var resource = new FakeResource(() => container);

        Assert.That(resource.ConnectionString, Is.Empty);

        await resource.StartAsync();

        Assert.That(resource.ConnectionString, Is.EqualTo("fake://host:1234"));
    }

    [Test]
    public async Task StartAsync_ShouldThrowAndReleaseTheBuiltContainerWhenItFails()
    {
        var container = new FakeContainer { Failure = new InvalidOperationException("no container runtime") };
        var resource = new FakeResource(() => container);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await resource.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("no container runtime"));
            Assert.That(container.DisposeCount, Is.EqualTo(1));
            Assert.That(resource.IsStarted, Is.False);
            Assert.That(resource.ConnectionString, Is.Empty);
        });
    }

    [Test]
    public async Task StartAsync_ShouldMakeConcurrentCallersAwaitTheSameStart()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var container = new FakeContainer { StartGate = gate.Task, ConnectionString = "fake://concurrent" };
        var resource = new FakeResource(() => container);

        var first = resource.StartAsync().AsTask();
        await container.Started!.Task;
        var second = resource.StartAsync().AsTask();

        Assert.That(second.IsCompleted, Is.False, "A concurrent caller must not return before the container is up.");

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(container.StartCount, Is.EqualTo(1), "Concurrent starts share one container start.");
            Assert.That(resource.ConnectionString, Is.EqualTo("fake://concurrent"),
                "Every caller must see the connection string once its start returns.");
            Assert.That(resource.IsStarted, Is.True);
        });
    }

    [Test]
    public async Task StartAsync_ShouldRetryWhenTheBuilderThrowsOnce()
    {
        var attempts = 0;
        var container = new FakeContainer { ConnectionString = "fake://retry-after-build" };
        var resource = new FakeResource(() =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("builder failed");
            }

            return container;
        });

        Assert.ThrowsAsync<InvalidOperationException>(async () => await resource.StartAsync());
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsStarted, Is.False, "A failed build must not mark the resource started.");
            Assert.That(resource.ConnectionString, Is.Empty);
        });

        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(resource.IsStarted, Is.True);
            Assert.That(resource.ConnectionString, Is.EqualTo("fake://retry-after-build"));
        });
    }

    [Test]
    public async Task StartAsync_ShouldResetWhenTheConnectionStringAccessorThrows()
    {
        var container = new FakeContainer { ConnectionString = "fake://ok" };
        var accessorCalls = 0;
        var resource = new FakeResource(
            () => container,
            value =>
            {
                accessorCalls++;
                return accessorCalls == 1
                    ? throw new InvalidOperationException("connection string unavailable")
                    : value.ConnectionString;
            });

        Assert.ThrowsAsync<InvalidOperationException>(async () => await resource.StartAsync());
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsStarted, Is.False, "A failed accessor must leave the resource retryable.");
            Assert.That(container.DisposeCount, Is.EqualTo(1));
        });

        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsStarted, Is.True);
            Assert.That(resource.ConnectionString, Is.EqualTo("fake://ok"));
            Assert.That(container.DisposeCount, Is.EqualTo(1), "The retried container is the live one.");
        });
    }

    [Test]
    public async Task StartAsync_ShouldBuildAFreshContainerWhenRetriedAfterFailure()
    {
        var failed = new FakeContainer { Failure = new InvalidOperationException("no container runtime") };
        var fresh = new FakeContainer { ConnectionString = "fake://retry" };
        var built = new Queue<FakeContainer>([failed, fresh]);
        var resource = new FakeResource(() => built.Dequeue());

        Assert.ThrowsAsync<InvalidOperationException>(async () => await resource.StartAsync());
        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.Empty);
            Assert.That(failed.StartCount, Is.EqualTo(1));
            Assert.That(failed.DisposeCount, Is.EqualTo(1));
            Assert.That(fresh.StartCount, Is.EqualTo(1));
            Assert.That(fresh.DisposeCount, Is.Zero);
            Assert.That(resource.ConnectionString, Is.EqualTo("fake://retry"));
        });
    }

    [Test]
    public async Task ReleaseAsync_AndDisposeAsync_ShouldReleaseTheContainerOnce()
    {
        var container = new FakeContainer();
        var resource = new FakeResource(() => container);
        var builder = new ProtoHostBuilder().AddResource(resource);

        await using (var host = builder.Build())
        {
            await host.StartAsync();
            var context = await host.StartTestAsync("release container", TestMethods.Placeholder);
            context.RegisterResource(ProtoResource.From(
                "container:release-again",
                "test",
                "Releases the container resource while the test ends",
                (release, _) => resource.ReleaseAsync(release)));
            await resource.StartAsync();

            // Ends the test: the test-scoped resource releases the container resource through the
            // release path once. Host disposal releases it again through the run resource store.
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();
        }

        await resource.DisposeAsync();
        await resource.DisposeAsync();

        Assert.That(container.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StartAsync_AfterRelease_ShouldStartAFreshContainer()
    {
        var first = new FakeContainer { ConnectionString = "fake://first" };
        var second = new FakeContainer { ConnectionString = "fake://second" };
        var built = new Queue<FakeContainer>([first, second]);
        var resource = new FakeResource(() => built.Dequeue());

        await resource.StartAsync();
        await resource.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(resource.ConnectionString, Is.Empty, "a released container's endpoint must not stay readable");
            Assert.That(resource.IsStarted, Is.False);
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        });

        // A retry after a failed run start re-owns the infrastructure it starts again, so the new
        // ownership period must be startable and releasable like the first.
        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second.StartCount, Is.EqualTo(1));
            Assert.That(resource.IsStarted, Is.True);
            Assert.That(resource.ConnectionString, Is.EqualTo("fake://second"));
        });

        await resource.DisposeAsync();
        Assert.That(second.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StartAsync_ShouldNotLeakAContainerWhenDisposedWhileStarting()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var container = new FakeContainer { StartGate = gate.Task };
        var resource = new FakeResource(() => container);

        var starting = resource.StartAsync().AsTask();
        await container.Started!.Task;

        var disposal = resource.DisposeAsync().AsTask();
        gate.SetResult();

        Assert.ThrowsAsync<ObjectDisposedException>(async () => await starting);
        await disposal;

        Assert.Multiple(() =>
        {
            Assert.That(container.StartCount, Is.EqualTo(1));
            Assert.That(container.DisposeCount, Is.EqualTo(1), "The container started during disposal must still be released.");
            Assert.That(resource.IsStarted, Is.False);
        });
    }

    [Test]
    public async Task TryStart_ShouldLeaveTheCandidateRetryableAfterAFailure()
    {
        var failed = new FakeContainer { Failure = new InvalidOperationException("no container runtime") };
        var fresh = new FakeContainer { ConnectionString = "fake://retry" };
        var built = new Queue<FakeContainer>([failed, fresh]);
        var resource = new FakeResource(() => built.Dequeue());

        var result = resource.TryStart();
        Assert.Multiple(() =>
        {
            Assert.That(result.Started, Is.False);
            Assert.That(result.Error, Is.EqualTo("InvalidOperationException: no container runtime"));
        });

        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsStarted, Is.True);
            Assert.That(resource.ConnectionString, Is.EqualTo("fake://retry"));
            Assert.That(failed.StartCount, Is.EqualTo(1));
            Assert.That(failed.DisposeCount, Is.EqualTo(1));
            Assert.That(fresh.StartCount, Is.EqualTo(1));
        });

        await resource.DisposeAsync();
        Assert.That(fresh.DisposeCount, Is.EqualTo(1));
    }



    [Test]
    public async Task StartAsync_ShouldAwaitReadinessAndRecordEvidence()
    {
        var attempts = 0;
        var container = new FakeContainer();
        var resource = new ReadinessResource(
            () => container,
            "port answers",
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromResult(attempts >= 3);
            },
            TimeSpan.FromSeconds(5));

        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(resource.IsStarted, Is.True);
            Assert.That(resource.StartupEvidence["readiness.port answers.attempts"], Is.EqualTo("3"));
            Assert.That(resource.StartupEvidence, Contains.Key("readiness.port answers.waitedMs"));
        });
    }

    [Test]
    public async Task StartAsync_WhenReadinessNeverArrives_ShouldFailAndReleaseTheContainer()
    {
        var ready = false;
        var container = new FakeContainer();
        var resource = new ReadinessResource(
            () => container,
            "port answers",
            (_, _) => ValueTask.FromResult(ready),
            TimeSpan.FromMilliseconds(150));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await resource.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("port answers"));
            Assert.That(exception.Message, Does.Contain("not satisfied"));
            Assert.That(container.DisposeCount, Is.EqualTo(1), "a container that never became ready is released");
            Assert.That(resource.IsStarted, Is.False);
        });

        // The failure left the resource retryable, like any other failed start.
        ready = true;
        await resource.StartAsync();
        Assert.That(resource.IsStarted, Is.True);
    }

    [Test]
    public async Task StartAsync_UnderAHost_ShouldUseTheRunReadinessTimeout()
    {
        var container = new FakeContainer();
        var resource = new ReadinessResource(
            () => container,
            "port answers",
            (_, _) => ValueTask.FromResult(false),
            TimeSpan.FromSeconds(30));
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureReadiness(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(200);
            options.Interval = TimeSpan.FromMilliseconds(20);
        });
        builder.AddInfrastructure(
            "NeverReady",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider("container", resource)),
            "ConnectionStrings:NeverReady");
        await using var host = builder.Build();

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Does.Contain("0.2s").Or.Contain("0,2s"),
                "ConfigureReadiness governs the container wait, not the container's private 30 s default");
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(container.DisposeCount, Is.EqualTo(1), "a container that never became ready is released");
        });
    }

    [Test]
    public async Task Release_ShouldBoundTheWaitForAnInFlightStartAndRecordTheAbandonedOne()
    {
        // Release sets the released flag, awaits the racing start under a
        // bound, and records the start still running instead of returning as if nothing were.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var container = new FakeContainer { StartGate = gate.Task };
        var resource = new FakeResource(() => container);
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        var context = await host.StartTestAsync("abandoned container start", TestMethods.Placeholder);
        context.RegisterResource(ProtoResource.From(
            "container:abandoned-start",
            "container",
            "Releases the container resource while its start is in flight",
            (release, _) => resource.ReleaseAsync(release)));

        var starting = resource.StartAsync().AsTask();
        await container.Started!.Task;

        var stopwatch = Stopwatch.StartNew();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        stopwatch.Stop();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(candidate => candidate.Kind == "container.start.abandoned");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)),
                "the release is bounded instead of hanging on the start");
            Assert.That(starting.IsCompleted, Is.False, "the abandoned start is not adopted by the release");
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(entry.Attributes["container.release_bound_ms"], Is.EqualTo("5000"));
        }
    }

    private sealed class ReadinessResource : ProtoContainerResource<FakeContainer>
    {
        public ReadinessResource(
            Func<FakeContainer> build,
            string name,
            Func<FakeContainer, CancellationToken, ValueTask<bool>> ready,
            TimeSpan timeout)
            : base(
                build,
                (container, cancellationToken) => container.StartAsync(cancellationToken),
                container => container.ConnectionString)
        {
            ReadinessTimeout = timeout;
            ReadinessInterval = TimeSpan.FromMilliseconds(10);
            ReadyWhen(name, ready);
        }

        public override string Id => "container:readiness";

        public override string Kind => "container";

        public override string Description => "Readiness fake container";
    }

    [Test]
    public async Task ReadyWhenTcp_ShouldWaitForThePortReadyOnSelects()
    {
        var port = TestNetworking.FreePort();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var container = new FakeContainer();
        var resource = new TcpReadinessResource(() => container);
        resource.ReadyOn(port);

        await resource.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsStarted, Is.True, "the overridden port is the one the check waits for");
            Assert.That(resource.StartupEvidence, Contains.Key("readiness.endpoint answers.attempts"));
        });

        await resource.DisposeAsync();
    }

    [Test]
    public async Task ReadyOn_ShouldValidateThePortAndRejectChangesAfterStart()
    {
        var container = new FakeContainer();
        var resource = new FakeResource(() => container);

        Assert.Throws<ArgumentOutOfRangeException>(() => resource.ReadyOn(0));
        Assert.That(resource.ReadyOn(1234), Is.SameAs(resource));

        await resource.StartAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => resource.ReadyOn(2345));
        Assert.That(exception!.Message, Does.Contain("before the run starts"));
        await resource.DisposeAsync();
    }

    private sealed class TcpReadinessResource : ProtoContainerResource<FakeContainer>
    {
        public TcpReadinessResource(Func<FakeContainer> build)
            : base(
                build,
                (container, cancellationToken) => container.StartAsync(cancellationToken),
                container => container.ConnectionString)
        {
            ReadinessTimeout = TimeSpan.FromSeconds(5);
            ReadinessInterval = TimeSpan.FromMilliseconds(10);
            ReadyWhenTcp("endpoint answers", defaultPort: 5432, (_, port) => ("127.0.0.1", port));
        }

        public override string Id => "container:tcp-readiness";

        public override string Kind => "container";

        public override string Description => "TCP readiness fake container";
    }

    private sealed class FakeResource(
        Func<FakeContainer> build,
        Func<FakeContainer, string>? connectionString = null)
        : ProtoContainerResource<FakeContainer>(
            build,
            (container, cancellationToken) => container.StartAsync(cancellationToken),
            connectionString ?? (container => container.ConnectionString))
    {
        public override string Id => "container:fake";

        public override string Kind => "container";

        public override string Description => "Fake container";

        public ContainerStartResult<FakeResource> TryStart() => TryStartContainer(this);
    }

    private sealed class FakeContainer : IAsyncDisposable
    {
        public string ConnectionString { get; set; } = string.Empty;

        public Exception? Failure { get; init; }

        public Task? StartGate { get; init; }

        public TaskCompletionSource? Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            Started?.TrySetResult();
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            return StartGate ?? Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
