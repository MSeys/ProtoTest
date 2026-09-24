namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

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
    public async Task DisposeAsync_ShouldLeaveSharedClientsUndisposed()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var owned = new DisposableClient();
        var shared = new DisposableClient();
        var context = CreateContext(rootProvider);
        context.RegisterClient(owned, "Owned");
        context.RegisterClient(shared, "Shared", ProtoClientOwnership.Caller);

        Assert.That(context.Client<DisposableClient>("Shared"), Is.SameAs(shared));
        await context.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(owned.DisposeCount, Is.EqualTo(1));
            Assert.That(shared.DisposeCount, Is.Zero);
        });
    }

    [Test]
    public async Task RegisterClient_ShouldRejectRegistrationAfterDisposalStarts()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(rootProvider);
        await context.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => context.RegisterClient(new DisposableClient()));
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

    [Test]
    public async Task ClientCompletion_ShouldRunOnceForAnAliasedClient()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(rootProvider);
        var client = new CompletingClient();
        context.RegisterClient(client, "Rest:Api", ProtoClientOwnership.Caller);
        context.RegisterClientAlias(typeof(CompletingClient), "Api", client);

        await new ProtoClientCompletionHook().AfterTestAsync(context);

        Assert.That(client.CompletionCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ClientCompletion_ShouldRunInReverseRegistrationOrder()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(rootProvider);
        var completed = new List<string>();
        context.RegisterClient(new OrderingCompletion("first", completed), "First", ProtoClientOwnership.Caller);
        context.RegisterClient(new OrderingCompletion("second", completed), "Second", ProtoClientOwnership.Caller);

        await new ProtoClientCompletionHook().AfterTestAsync(context);

        Assert.That(completed, Is.EqualTo(new[] { "second", "first" }),
            "the completion hook relies on the registry snapshot being in registration order");
    }

    [Test]
    public async Task ClientResolution_ShouldSkipExcludedClientAtEveryLookupName()
    {
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(rootProvider);
        var transport = new DisposableClient();
        context.RegisterClient(transport, "Rest:App:Api", ProtoClientOwnership.Caller);
        context.RegisterClientAlias(typeof(DisposableClient), "App:Api", transport);
        context.RegisterClientAlias(typeof(DisposableClient), "Rest:Api", transport);
        context.RegisterClientAlias(typeof(DisposableClient), "Api", transport);

        var resolution = ProtoClientResolution.Find<DisposableClient>(
            context, "Rest", "Api", "App:Api", transport);

        Assert.That(resolution.Client, Is.Null);
    }

    private static ProtoExecutionContext CreateContext(ServiceProvider rootProvider)
        => new(
            "Test",
            rootProvider.CreateScope(),
            "00001",
            TestMethods.Placeholder);

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

    private sealed class CompletingClient : IProtoClientCompletion
    {
        public int CompletionCount { get; private set; }

        public ValueTask CompleteAsync()
        {
            CompletionCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderingCompletion(string name, List<string> completed) : IProtoClientCompletion
    {
        public ValueTask CompleteAsync()
        {
            completed.Add(name);
            return ValueTask.CompletedTask;
        }
    }
}
