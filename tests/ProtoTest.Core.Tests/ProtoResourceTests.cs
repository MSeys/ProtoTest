namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public class ProtoResourceTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    [SetUp]
    public void SetUp()
    {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    [Test]
    public async Task ReleaseAll_ShouldReleaseResourcesInReverseRegistrationOrder()
    {
        // Arrange
        var released = new List<string>();
        var context = CreateContext();
        context.RegisterResource(new TrackedResource("first", released));
        context.RegisterResource(new TrackedResource("second", released));
        context.RegisterResource(new TrackedResource("third", released));

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.That(released, Is.EqualTo(new[] { "third", "second", "first" }));
    }

    [Test]
    public void RegisterResource_ShouldRejectDuplicateIds()
    {
        // Arrange
        var context = CreateContext();
        context.RegisterResource(new TrackedResource("customer:42", []));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => context.RegisterResource(new TrackedResource("customer:42", [])));
        Assert.That(exception!.Message, Does.Contain("customer:42"));
    }

    [Test]
    public async Task ReleaseResourceAsync_ShouldReleaseOnceAndSkipItDuringTeardown()
    {
        // Arrange
        var released = new List<string>();
        var context = CreateContext();
        context.RegisterResource(new TrackedResource("early", released));

        // Act
        var first = await context.ReleaseResourceAsync("early");
        var second = await context.ReleaseResourceAsync("early");
        var unknown = await context.ReleaseResourceAsync("missing");
        await context.DisposeAsync();

        // Assert
        Assert.That(released, Is.EqualTo(new[] { "early" }));
        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(unknown, Is.False);
    }

    [Test]
    public async Task ReleaseResourceAsync_ConcurrentCalls_ShouldRunTheCallbackOnce()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = 0;
        var context = CreateContext();
        context.RegisterResource(new ProtoResource(
            "blocking",
            "test",
            "Blocks while releasing",
            async _ =>
            {
                Interlocked.Increment(ref releases);
                entered.TrySetResult();
                await release.Task;
            }));

        // Act: the second call must lose the race while the first is still inside the callback.
        var first = context.ReleaseResourceAsync("blocking").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await context.ReleaseResourceAsync("blocking");
        release.SetResult();
        var firstResult = await first;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(firstResult, Is.True);
            Assert.That(second, Is.False);
            Assert.That(Volatile.Read(ref releases), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ReleaseResourceAsync_WhileTheReleaseIsInFlight_ShouldReportReleasing()
    {
        // The release state is one enum, so a snapshot mid-release says so.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = CreateContext();
        context.RegisterResource(new ProtoResource(
            "blocking",
            "test",
            "Blocks while releasing",
            async _ =>
            {
                entered.TrySetResult();
                await release.Task;
            }));

        // Act
        var pending = context.ReleaseResourceAsync("blocking").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(
            context.Resources.Single().State,
            Is.EqualTo(ProtoResourceState.Releasing),
            "a snapshot between begin and end reports the release as in flight");

        release.SetResult();
        await pending;
        Assert.That(context.Resources.Single().State, Is.EqualTo(ProtoResourceState.Released));
    }

    [Test]
    public async Task ReleaseAll_ShouldContinueAfterAFailedReleaseAndReportIt()
    {
        // Arrange
        var released = new List<string>();
        var context = CreateContext();
        context.RegisterResource(new TrackedResource("healthy", released));
        context.RegisterResource(new FailingResource("broken"));

        // Act
        var exception = Assert.ThrowsAsync<AggregateException>(async () => await context.DisposeAsync());

        // Assert
        Assert.That(released, Is.EqualTo(new[] { "healthy" }));
        Assert.That(exception!.InnerExceptions.Single().Message, Is.EqualTo("Release of 'broken' failed."));
        var broken = context.Resources.Single(resource => resource.Id == "broken");
        Assert.That(broken.State, Is.EqualTo(ProtoResourceState.ReleaseFailed));
        Assert.That(broken.Error, Is.EqualTo("Release of 'broken' failed."));
    }

    [Test]
    public async Task Dispose_ShouldReleaseEverythingInReverseCreationOrder()
    {
        // Arrange: clients are registered during setup, resources while the test runs.
        var events = new List<string>();
        var context = CreateContext();
        context.RegisterClient(new TrackedClient(events), "Tracked");
        context.RegisterResource(new TrackedResource("database", events));

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.That(events, Is.EqualTo(new[] { "database", "dispose:Tracked" }));
    }

    [Test]
    public async Task Dispose_ShouldLeaveSharedClientsUndisposedButStillRecorded()
    {
        // Arrange
        var released = new List<string>();
        var context = CreateContext();
        context.RegisterClient(new TrackedClient(released), "Shared", ProtoClientOwnership.Caller);

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.That(released, Is.Empty);
        var client = context.Resources.Single(resource => resource.Kind == "client");
        Assert.That(client.State, Is.EqualTo(ProtoResourceState.Released));
        Assert.That(client.Description, Does.Contain("Shared client"));
    }

    [Test]
    public void Resources_ShouldExposeRegistrationOrderStateAndDescription()
    {
        // Arrange
        var context = CreateContext();
        context.RegisterResource(ProtoResource.From(
            "environment:demo",
            "environment",
            "Demo environment",
            (_, _) => ValueTask.CompletedTask));

        // Act
        var resources = context.Resources;

        // Assert
        Assert.That(resources, Has.Count.EqualTo(1));
        var resource = resources[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resource.Id, Is.EqualTo("environment:demo"));
            Assert.That(resource.Kind, Is.EqualTo("environment"));
            Assert.That(resource.Description, Is.EqualTo("Demo environment"));
            Assert.That(resource.State, Is.EqualTo(ProtoResourceState.Registered));
        }
    }

    [Test]
    public async Task OwnedResources_ShouldReachTheRunReportAndSkipHealthyClients()
    {
        // Arrange
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("Ownership", "00009", TestMethods.Placeholder);
        context.RegisterClient(new TrackedClient([]), "Shared", ProtoClientOwnership.Caller);
        context.RegisterResource(ProtoResource.From(
            "database:connection",
            "database",
            "Test database",
            (_, _) => ValueTask.CompletedTask));

        // Act
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert: framework client plumbing is trace content, not report content.
        var resources = sink.Items.Where(item => item.Kind == ProtoReportItemKinds.Resource).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resources, Has.Exactly(1).Items);
            Assert.That(resources[0].Identifier, Is.EqualTo("database:connection"));
            Assert.That(resources[0].Status, Is.EqualTo(ProtoReportStatus.Success));
            Assert.That(resources[0].DisplayGroup, Is.EqualTo("Ownership"));
        }
    }

    [Test]
    public async Task FailedClientRelease_ShouldReachTheRunReport()
    {
        // Arrange
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("Broken", "00010", TestMethods.Placeholder);
        context.RegisterClient(new FailingClient(), "Broken");

        // Act
        Assert.ThrowsAsync<AggregateException>(() =>
            host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException("The test failed."))));

        // Assert: a failed teardown is report content even for framework-managed plumbing.
        await host.StopAsync();
        var resources = sink.Items.Where(item => item.Kind == ProtoReportItemKinds.Resource).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resources, Has.Exactly(1).Items);
            Assert.That(resources[0].Status, Is.EqualTo(ProtoReportStatus.Error));
            Assert.That(resources[0].Metadata!["resource.error"], Is.EqualTo("Client release failed."));
        }
    }

    [Test]
    public async Task ReleaseResourceAsync_WhenTheCallbackFails_ThrowsAndDisposeCountsThatFailure()
    {
        // Arrange: the failure is recorded, then thrown. Dispose does not run the callback again,
        // but the same exception is still part of the cleanup aggregate.
        var releases = 0;
        var failure = new InvalidOperationException("release failed");
        var context = CreateContext();
        context.RegisterResource("broken", "audit", "Throws on release", _ =>
        {
            Interlocked.Increment(ref releases);
            return ValueTask.FromException(failure);
        });

        // Act
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.ReleaseResourceAsync("broken"));
        var again = await context.ReleaseResourceAsync("broken");
        var dispose = Assert.ThrowsAsync<AggregateException>(async () => await context.DisposeAsync());
        var secondDispose = Assert.ThrowsAsync<AggregateException>(async () => await context.DisposeAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(again, Is.False, "a release that already failed has started; it is not run again");
            Assert.That(context.Resources.Single().State, Is.EqualTo(ProtoResourceState.ReleaseFailed));
            Assert.That(context.Resources.Single().Error, Is.EqualTo("release failed"));
            Assert.That(dispose!.InnerExceptions.Single(), Is.SameAs(failure));
            Assert.That(secondDispose!.InnerExceptions.Single(), Is.SameAs(failure),
                "a second dispose observes the same completion");
            Assert.That(releases, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task EarlyReleaseFailure_IsATeardownFindingAndDoesNotFailTheRun()
    {
        // The failure reaches cleanup the same way a release that fails during dispose does. The
        // test outcome becomes Partial, which is the existing teardown policy, and the run stays
        // green unless a gate is configured.
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("BrokenRelease", "00011", TestMethods.Placeholder);
        context.RegisterResource("broken", "audit", "Throws on release",
            _ => ValueTask.FromException(new InvalidOperationException("release failed")));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await context.ReleaseResourceAsync("broken"));
        var teardown = Assert.ThrowsAsync<AggregateException>(
            async () => await host.CompleteTestAsync(ProtoTestResult.Passed));

        var test = host.Trace.Snapshot().Tests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(teardown!.InnerExceptions.Single().Message, Is.EqualTo("release failed"));
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(test.Error, Is.Null, "the cleanup failure is not the test's own error");
            Assert.That(
                test.Record!.Findings,
                Has.Some.Matches<ProtoTraceFindingRecord>(finding =>
                    finding.Category == "Teardown" && finding.Message.Contains("Teardown failed")));
        }

        Assert.DoesNotThrowAsync(async () => await host.StopAsync());
    }

    [Test]
    public async Task DisposeAsync_WaitsForAnInFlightReleaseBeforeDisposingTheScope()
    {
        // Arrange
        using var root = new ServiceCollection().BuildServiceProvider();
        var scope = new TrackingScope(root.CreateScope());
        var context = new ProtoExecutionContext("TestMethod", scope, "00012", TestMethods.Placeholder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeDisposedDuringRelease = -1;
        context.RegisterResource("slow", "audit", "Blocked release", async _ =>
        {
            entered.TrySetResult();
            await gate.Task;
            scopeDisposedDuringRelease = scope.DisposeCount;
        });

        // Act
        var pending = context.ReleaseResourceAsync("slow").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = context.DisposeAsync().AsTask();
        var second = context.DisposeAsync().AsTask();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(first.IsCompleted, Is.False, "dispose waits for the release that is already running");
                Assert.That(second.IsCompleted, Is.False, "a second dispose waits for the same completion");
            });
        }
        finally
        {
            gate.TrySetResult();
        }

        await first;
        await second;
        await pending;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scopeDisposedDuringRelease, Is.Zero, "the scope outlives the release callback");
            Assert.That(scope.DisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ReleaseCallback_ThatDisposesItsOwnContext_ShouldFailInsteadOfDeadlocking()
    {
        // The callback resumes after dispose has started waiting for it. Waiting for dispose from
        // there would deadlock; the callback must be told no.
        var context = CreateContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.RegisterResource("slow", "audit", "Disposes its context", async _ =>
        {
            entered.TrySetResult();
            await gate.Task;
            await context.DisposeAsync();
        });

        var pending = context.ReleaseResourceAsync("slow").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposing = context.DisposeAsync().AsTask();
        Assert.That(disposing.IsCompleted, Is.False, "dispose is waiting for the in-flight release");
        gate.TrySetResult();

        var releaseError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        var disposeError = Assert.ThrowsAsync<AggregateException>(async () =>
            await disposing.WaitAsync(TimeSpan.FromSeconds(5)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(releaseError!.Message, Is.EqualTo("A release callback cannot dispose its own context."));
            Assert.That(disposeError!.InnerExceptions.Single(), Is.SameAs(releaseError));
            Assert.That(context.Resources.Single().State, Is.EqualTo(ProtoResourceState.ReleaseFailed));
        }
    }

    [Test]
    public async Task ReleaseCallback_ThatReleasesItsOwnResource_ShouldFailInsteadOfDeadlocking()
    {
        var context = CreateContext();
        var calls = 0;
        context.RegisterResource("box", "audit", "Releases itself", async _ =>
        {
            Interlocked.Increment(ref calls);
            await context.ReleaseResourceAsync("box");
        });

        var releaseError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.ReleaseResourceAsync("box").AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        var disposeError = Assert.ThrowsAsync<AggregateException>(async () =>
            await context.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(releaseError!.Message, Is.EqualTo("A release callback cannot release its own resource 'box'."));
            Assert.That(disposeError!.InnerExceptions.Single(), Is.SameAs(releaseError));
            Assert.That(calls, Is.EqualTo(1), "the callback ran once and was not released again");
        }
    }

    [Test]
    public async Task ReleaseCallback_MayReleaseADifferentResource()
    {
        var context = CreateContext();
        var order = new List<string>();
        context.RegisterResource("outer", "audit", "Releases the other resource", async _ =>
        {
            order.Add("outer");
            Assert.That(await context.ReleaseResourceAsync("inner"), Is.True);
            order.Add("outer-after");
        });
        context.RegisterResource("inner", "audit", "Released by the other callback", _ =>
        {
            order.Add("inner");
            return ValueTask.CompletedTask;
        });

        Assert.That(await context.ReleaseResourceAsync("outer"), Is.True);
        await context.DisposeAsync();

        Assert.That(order, Is.EqualTo(new[] { "outer", "inner", "outer-after" }));
    }

    private ProtoExecutionContext CreateContext()
        => new("TestMethod", _scope, "00001", TestMethods.Placeholder);


    private sealed class TrackedResource(string id, List<string> released) : IProtoResource
    {
        public string Id { get; } = id;

        public string Kind => "test";

        public string Description => $"Tracked resource {Id}";

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
        {
            released.Add(Id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingResource(string id) : IProtoResource
    {
        public string Id { get; } = id;

        public string Kind => "test";

        public string Description => $"Failing resource {Id}";

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
            => throw new InvalidOperationException($"Release of '{Id}' failed.");
    }

    private sealed class TrackedClient(List<string> events, string name = "Tracked") : IDisposable
    {
        public void Dispose() => events.Add($"dispose:{name}");
    }

    private sealed class FailingClient : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("Client release failed.");
    }

    private sealed class TrackingScope(IServiceScope inner) : IServiceScope
    {
        public int DisposeCount { get; private set; }

        public IServiceProvider ServiceProvider => inner.ServiceProvider;

        public void Dispose()
        {
            DisposeCount++;
            inner.Dispose();
        }
    }
}
