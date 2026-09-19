namespace ProtoTest.Testcontainers.Tests;

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
            var context = await host.StartTestAsync("release container", TestMethod());
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
    public async Task StartAsync_ShouldThrowObjectDisposedExceptionAfterRelease()
    {
        var container = new FakeContainer();
        var resource = new FakeResource(() => container);

        await resource.DisposeAsync();

        Assert.ThrowsAsync<ObjectDisposedException>(async () => await resource.StartAsync());
        Assert.That(container.StartCount, Is.Zero, "A released resource must not start.");    }

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

        Assert.That(resource.TryStart(out var error), Is.False);
        Assert.That(error, Is.EqualTo("InvalidOperationException: no container runtime"));

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

    private static MethodInfo TestMethod()
        => typeof(ProtoContainerResourceTests).GetMethod(nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
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

        public bool TryStart(out string? error) => TryStartContainer(this, out error);
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
