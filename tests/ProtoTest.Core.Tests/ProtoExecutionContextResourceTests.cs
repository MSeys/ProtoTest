namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class ProtoExecutionContextResourceTests
{
    [Test]
    public async Task RegisterClient_ShouldRejectDuplicateTypeAndName()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(rootProvider);
        context.RegisterClient(new DisposableClient(), "Api");

        Assert.Throws<InvalidOperationException>(() =>
            context.RegisterClient(new DisposableClient(), "api"));

        await context.DisposeAsync();
    }

    [Test]
    public async Task DisposeAsync_ShouldBeIdempotent()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var client = new DisposableClient();
        var context = CreateContext(rootProvider);
        context.RegisterClient(client);

        await context.DisposeAsync();
        await context.DisposeAsync();

        Assert.That(client.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void DisposeAsync_ShouldAttemptAllClients_WhenOneFails()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var first = new FailingClient();
        var second = new DisposableClient();
        var context = CreateContext(rootProvider);
        context.RegisterClient(first, "First");
        context.RegisterClient(second, "Second");

        var exception = Assert.ThrowsAsync<AggregateException>(async () => await context.DisposeAsync());

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(first.DisposeCount, Is.EqualTo(1));
        Assert.That(second.DisposeCount, Is.EqualTo(1));
    }

    private static ProtoExecutionContext CreateContext(ServiceProvider rootProvider)
        => new(
            "Test",
            rootProvider.CreateScope(),
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

    private class DisposableClient : IDisposable
    {
        public int DisposeCount { get; private set; }

        public virtual void Dispose() => DisposeCount++;
    }

    private sealed class FailingClient : DisposableClient
    {
        public override void Dispose()
        {
            base.Dispose();
            throw new InvalidOperationException("Expected disposal failure.");
        }
    }
}
