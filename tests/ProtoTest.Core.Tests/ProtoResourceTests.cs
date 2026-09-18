namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

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
        context.RegisterClient(new TrackedClient(released), "Shared", disposeWithContext: false);

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
        var context = await host.StartTestAsync("Ownership", "00009", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        context.RegisterClient(new TrackedClient([]), "Shared", disposeWithContext: false);
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
        var context = await host.StartTestAsync("Broken", "00010", (MethodInfo)MethodInfo.GetCurrentMethod()!);
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

    private ProtoExecutionContext CreateContext()
        => new("TestMethod", _scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

    private sealed class CapturingSink : IProtoSink
    {
        private ProtoReportItem[] _items = [];

        public IReadOnlyList<ProtoReportItem> Items => _items;

        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
        {
            _items = [.. items];
            return Task.CompletedTask;
        }
    }

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
}
