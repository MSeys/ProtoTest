namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public class ReplaceClientTests
{
    [Test]
    public void ReplaceClient_ShouldServeTheReplacementForTheName()
    {
        // Arrange
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        using var scope = rootProvider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", TestMethods.Placeholder);
        var original = new TrackingClient();
        var replacement = new TrackingClient();
        context.RegisterClient(original, "Default");

        // Act
        context.ReplaceClient(replacement, "Default");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Client<TrackingClient>("Default"), Is.SameAs(replacement));
            Assert.That(context.TryClient<TrackingClient>("Default"), Is.SameAs(replacement));
        }
    }

    [Test]
    public void ReplaceClient_WhenNothingIsRegistered_ShouldThrowNamingTheClient()
    {
        // Arrange
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        using var scope = rootProvider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", TestMethods.Placeholder);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => context.ReplaceClient(new TrackingClient(), "Missing"));

        // Assert
        Assert.That(exception!.Message, Does.Contain("'Missing'"));
    }

    [Test]
    public async Task DisposeAsync_AfterReplaceClient_ShouldDisposeBothInstancesOnce()
    {
        // Arrange
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var scope = rootProvider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", TestMethods.Placeholder);
        var original = new TrackingClient();
        var replacement = new TrackingClient();
        context.RegisterClient(original, "Default");
        context.ReplaceClient(replacement, "Default");

        // Act
        await context.DisposeAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(original.Disposals, Is.EqualTo(1), "the replaced instance keeps its own teardown release");
            Assert.That(replacement.Disposals, Is.EqualTo(1), "the replacement is owned by the test");
        }
    }

    [Test]
    public async Task DisposeAsync_WhenTheRegisteredInstanceIsReplaced_ShouldKeepOneRelease()
    {
        // Arrange
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var scope = rootProvider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", TestMethods.Placeholder);
        var client = new TrackingClient();
        context.RegisterClient(client, "Default");

        // Act: replacing with the registered instance is a no-op, not a second ownership.
        context.ReplaceClient(client, "Default");
        var served = context.Client<TrackingClient>("Default");
        await context.DisposeAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(served, Is.SameAs(client), "the instance stays registered");
            Assert.That(client.Disposals, Is.EqualTo(1), "the same instance is disposed exactly once");
        }
    }

    private sealed class TrackingClient : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose()
        {
            Disposals++;
        }
    }
}
