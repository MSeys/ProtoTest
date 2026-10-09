namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class ClientIdentityTests
{
    [Test]
    public async Task RegisterClient_SameInstanceAndSameKey_ShouldNotOwnItTwice()
    {
        // The lookup key ignores case. The resource id keeps the first spelling, so "api" must not
        // allocate a second ordinal id.
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var client = new TrackingClient();
        context.RegisterClient(client, "Api");

        context.RegisterClient(client, "api");

        var owned = context.Resources.Where(resource => resource.Kind == "client").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Client<TrackingClient>("api"), Is.SameAs(client));
            Assert.That(owned, Has.Length.EqualTo(1));
            Assert.That(owned[0].Id, Is.EqualTo(ProtoClientTrace.Id(typeof(TrackingClient), "Api")));
        }

        await context.DisposeAsync();
        Assert.That(client.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task RegisterClient_SameInstanceWithDifferentOwnership_ShouldNameBoth()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var client = new TrackingClient();
        context.RegisterClient(client, "Api", ProtoClientOwnership.Context);

        var sameKey = Assert.Throws<InvalidOperationException>(() =>
            context.RegisterClient(client, "Api", ProtoClientOwnership.Caller));
        var otherName = Assert.Throws<InvalidOperationException>(() =>
            context.RegisterClient(client, "Alias", ProtoClientOwnership.Caller));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameKey!.Message, Does.Contain(nameof(ProtoClientOwnership.Context)));
            Assert.That(sameKey.Message, Does.Contain(nameof(ProtoClientOwnership.Caller)));
            Assert.That(otherName!.Message, Does.Contain(nameof(ProtoClientOwnership.Context)));
            Assert.That(otherName.Message, Does.Contain(nameof(ProtoClientOwnership.Caller)));
            Assert.That(context.TryClient<TrackingClient>("Alias"), Is.Null);
        }
    }

    [Test]
    public async Task RegisterClient_SameInstanceUnderTwoNames_ShouldDisposeOnce()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var client = new TrackingClient();
        context.RegisterClient(client, "Api");
        context.RegisterClient(client, "Alias");

        var ids = context.Resources.Where(resource => resource.Kind == "client").Select(resource => resource.Id).ToArray();
        Assert.That(ids, Is.EqualTo(new[] { ProtoClientTrace.Id(typeof(TrackingClient), "Api") }));
        Assert.That(context.Client<TrackingClient>("Alias"), Is.SameAs(client));

        await context.DisposeAsync();
        Assert.That(client.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task RegisterClient_SharedInstanceUnderTwoNames_ShouldStayUndisposed()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var client = new TrackingClient();
        context.RegisterClient(client, "Api", ProtoClientOwnership.Caller);
        context.RegisterClient(client, "Alias", ProtoClientOwnership.Caller);

        await context.DisposeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.Disposals, Is.Zero);
            Assert.That(context.Resources.Count(resource => resource.Kind == "client"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RegisterClient_SameInstanceAsInterfaceAndConcreteType_ShouldDisposeOnce()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var client = new TrackingClient();
        context.RegisterClient<IDisposable>(client, "Api");
        context.RegisterClient<TrackingClient>(client, "Api");

        var ids = context.Resources.Where(resource => resource.Kind == "client").Select(resource => resource.Id).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Client<IDisposable>("Api"), Is.SameAs(client));
            Assert.That(context.Client<TrackingClient>("Api"), Is.SameAs(client));
            Assert.That(ids, Is.EqualTo(new[] { ProtoClientTrace.Id(typeof(IDisposable), "Api") }));
        }

        await context.DisposeAsync();
        Assert.That(client.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task ReplaceClient_ShouldReleaseOnlyTheInstanceThatLostItsLastName()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var first = new TrackingClient();
        var second = new TrackingClient();
        var third = new TrackingClient();
        context.RegisterClient(first, "Api");
        context.RegisterClient(first, "Alias");
        context.RegisterClient(second, "Other");

        // "Api" now points at an instance that is already owned. "Alias" still references first.
        context.ReplaceClient(second, "Api");
        context.ReplaceClient(third, "Alias");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Client<TrackingClient>("Api"), Is.SameAs(second));
            Assert.That(context.Client<TrackingClient>("Other"), Is.SameAs(second));
            Assert.That(context.Client<TrackingClient>("Alias"), Is.SameAs(third));
            Assert.That(context.Resources.Count(resource => resource.Kind == "client"), Is.EqualTo(3));
        }

        await context.DisposeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Disposals, Is.EqualTo(1), "the instance no longer referenced is released once");
            Assert.That(second.Disposals, Is.EqualTo(1), "an instance another name still references is not released again");
            Assert.That(third.Disposals, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ReplaceClient_SameInstanceWithDifferentOwnership_ShouldNameBoth()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        await using var context = CreateContext(root);
        var owned = new TrackingClient();
        var shared = new TrackingClient();
        context.RegisterClient(owned, "Api");
        context.RegisterClient(shared, "Shared", ProtoClientOwnership.Caller);

        var sameName = Assert.Throws<InvalidOperationException>(() =>
            context.ReplaceClient(owned, "Api", ProtoClientOwnership.Caller));
        var alreadyOwned = Assert.Throws<InvalidOperationException>(() =>
            context.ReplaceClient(shared, "Api"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameName!.Message, Does.Contain(nameof(ProtoClientOwnership.Context)));
            Assert.That(sameName.Message, Does.Contain(nameof(ProtoClientOwnership.Caller)));
            Assert.That(alreadyOwned!.Message, Does.Contain(nameof(ProtoClientOwnership.Caller)));
            Assert.That(alreadyOwned.Message, Does.Contain(nameof(ProtoClientOwnership.Context)));
            Assert.That(context.Client<TrackingClient>("Api"), Is.SameAs(owned));
        }

        await context.DisposeAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owned.Disposals, Is.EqualTo(1));
            Assert.That(shared.Disposals, Is.Zero);
        }
    }

    private static ProtoExecutionContext CreateContext(ServiceProvider root)
        => new("Test", root.CreateScope(), "00021", TestMethods.Placeholder);

    private sealed class TrackingClient : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }
}
