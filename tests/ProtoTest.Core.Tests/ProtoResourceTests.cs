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

    private ProtoExecutionContext CreateContext()
        => new("TestMethod", _scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

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
}
