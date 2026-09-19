namespace ProtoTest.Messaging.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using System.Reflection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddMessaging_CalledTwice_ShouldRegisterOneInitializerAndOneRunResource()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddMessaging();
        builder.AddMessaging();

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoMessageBroker)),
                Is.EqualTo(1));
        });

        await host.StartAsync();
        var owned = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == "resource.owned")
            .Select(entry => entry.Attributes["resource.id"])
            .ToArray();
        Assert.That(owned, Is.EqualTo(new[] { "messaging:broker" }));

        var context = await host.StartTestAsync("messaging idempotent", TestMethod());
        var messages = context.Messaging();
        await messages.PublishAsync("invoices", "{\"id\":1}");
        var received = await messages.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":1}",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
        await host.StopAsync();
    }

    [Test]
    public async Task AddMessaging_AdapterOnASecondCall_ShouldReplaceTheInMemoryDefault()
    {
        var broker = new CountingBroker();
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        builder.AddMessaging(messaging => messaging.UseBroker(_ => broker));

        await using var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True,
            "an adapter added by the second call makes the Broker capability true");

        await host.StartAsync();
        var context = await host.StartTestAsync("messaging late adapter", TestMethod());
        var messages = context.Messaging();
        await messages.PublishAsync("invoices", "{\"id\":1}");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(broker.Published, Is.EqualTo(1), "the second call's adapter served the publish");
        Assert.That(broker.Disposed, Is.True, "the run resource released the adapter");
    }

    [Test]
    public async Task AddMessaging_CalledTwiceWithAdapters_ShouldKeepTheFirstAndReleaseItOnce()
    {
        var broker = new CountingBroker();
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddMessaging(messaging => messaging.UseBroker(_ => broker));
        builder.AddMessaging(messaging => messaging.UseBroker(
            _ => throw new InvalidOperationException("The second broker adapter must not register.")));

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
        });

        await host.StartAsync();
        var context = await host.StartTestAsync("messaging adapter idempotent", TestMethod());
        _ = context.Messaging();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(broker.Disposed, Is.True);
    }

    [Test]
    public async Task AddMessaging_WhenConfigureThrows_ShouldNotPreventALaterSuccessfulCall()
    {
        var broker = new CountingBroker();
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddMessaging(_ => throw new InvalidOperationException("configure exploded")));
        builder.AddMessaging(messaging => messaging.UseBroker(_ => broker));

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
        });

        await host.StartAsync();
        var owned = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == "resource.owned")
            .Select(entry => entry.Attributes["resource.id"])
            .ToArray();
        await host.StopAsync();
        Assert.That(owned, Is.EqualTo(new[] { "messaging:broker" }), "the run resource is registered once");
    }

    private sealed class CountingBroker : IProtoMessageBroker, IDisposable
    {
        public string Name => "Counting";

        public int Published { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
        {
            Published++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new FakeConsumer());

        public void Dispose() => Disposed = true;

        private sealed class FakeConsumer : IProtoMessageConsumer
        {
            public ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;

            public ValueTask<ProtoMessage> AwaitAsync(
                string destination,
                Func<ProtoMessage, bool> predicate,
                TimeSpan timeout,
                CancellationToken cancellationToken = default)
                => throw new TimeoutException("The counting broker never has messages.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static MethodInfo TestMethod()
        => typeof(RegistrationIdempotencyTests).GetMethod(
            nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
