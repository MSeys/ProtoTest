namespace ProtoTest.Messaging.Tests;

using System.Reflection;
using NUnit.Framework;
using ProtoTest.Core;

[TestFixture]
public sealed class MessagingTests
{
    [Test]
    public async Task Publish_ShouldBeAwaitedByPredicate()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging publish", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");
        var received = await messages.AwaitAsync(
            "invoices",
            message => message.Destination == "invoices",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
            Assert.That(test.Entries.Any(entry => entry.Kind == "messaging.publish"), Is.True);
            Assert.That(test.Entries.Single(entry => entry.Kind == "messaging.await").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(context.RecordedObservations.Any(observation => observation.Kind == "messaging.receive"), Is.True);
        });
    }

    [Test]
    public async Task Await_ShouldTimeOutWhenNothingMatches()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging timeout", TestMethod());
        var messages = context.Messaging();

        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await messages.AwaitAsync("invoices", _ => false, TimeSpan.FromMilliseconds(50)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        Assert.That(timeout!.Message, Does.Contain("invoices"));
    }

    [Test]
    public async Task MessagesFromEarlierTests_ShouldNotSatisfyLaterAwaits()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();

        var first = await host.StartTestAsync("messaging first", TestMethod());
        await first.Messaging().PublishAsync("invoices", "{\"id\":1}");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var second = await host.StartTestAsync("messaging second", TestMethod());
        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await second.Messaging().AwaitAsync(
                "invoices",
                message => message.Destination == "invoices",
                TimeSpan.FromMilliseconds(50)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        Assert.That(timeout!.Message, Does.Contain("invoices"));
    }

    [Test]
    public async Task ConsumersCreatedInOrder_ShouldNotSeeEachOthersMessages()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging consumers", TestMethod());
        var broker = context.Service<IProtoMessageBroker>();
        var messages = context.Messaging();

        var first = await broker.CreateConsumerAsync();
        context.RegisterResource("messaging:consumer:first", "consumer", "First consumer", _ => first.DisposeAsync());
        await first.PrepareAsync(["invoices"]);
        await messages.PublishAsync("invoices", "{\"id\":1}");

        var second = await broker.CreateConsumerAsync();
        context.RegisterResource("messaging:consumer:second", "consumer", "Second consumer", _ => second.DisposeAsync());
        await second.PrepareAsync(["invoices"]);
        await messages.PublishAsync("invoices", "{\"id\":2}");

        var secondOwn = await second.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":2}",
            TimeSpan.FromSeconds(2));
        var firstMessages = await first.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":2}",
            TimeSpan.FromSeconds(2));
        var missed = Assert.ThrowsAsync<TimeoutException>(async () =>
            await second.AwaitAsync(
                "invoices",
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromMilliseconds(50)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(missed!));
        Assert.Multiple(() =>
        {
            Assert.That(secondOwn.Payload, Is.EqualTo("{\"id\":2}"));
            Assert.That(firstMessages.Payload, Is.EqualTo("{\"id\":2}"));
            Assert.That(missed!.Message, Does.Contain("invoices"));
        });
    }

    [Test]
    public async Task ThrowingPredicate_ShouldNotHangOrLoseOtherWaiters()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging throwing predicate", TestMethod());
        var broker = context.Service<IProtoMessageBroker>();
        var messages = context.Messaging();

        var throwing = await broker.CreateConsumerAsync();
        context.RegisterResource("messaging:consumer:throwing", "consumer", "Throwing consumer", _ => throwing.DisposeAsync());
        var healthy = await broker.CreateConsumerAsync();
        context.RegisterResource("messaging:consumer:healthy", "consumer", "Healthy consumer", _ => healthy.DisposeAsync());
        await throwing.PrepareAsync(["invoices"]);
        await healthy.PrepareAsync(["invoices"]);

        var faulty = throwing.AwaitAsync(
            "invoices",
            _ => throw new InvalidOperationException("predicate exploded"),
            TimeSpan.FromSeconds(5));
        var healthyAwait = healthy.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":1}",
            TimeSpan.FromSeconds(2));

        await messages.PublishAsync("invoices", "{\"id\":1}");

        var received = await healthyAwait;
        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await faulty);

        await host.CompleteTestAsync(ProtoTestResult.Failed(failure!));
        Assert.Multiple(() =>
        {
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"),
                "the publish completed and the healthy waiter still matched");
            Assert.That(failure!.Message, Is.EqualTo("predicate exploded"),
                "the throwing predicate fails only the await that owns it");
        });
    }

    [Test]
    public async Task RepeatedAwaits_ShouldConsumeInsteadOfRedeliveringTheFirstMatch()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging consumption", TestMethod());
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "{\"id\":1}");
        await messages.PublishAsync("invoices", "{\"id\":2}");
        var first = await messages.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2));
        var second = await messages.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(first.Payload, Is.EqualTo("{\"id\":1}"));
            Assert.That(second.Payload, Is.EqualTo("{\"id\":2}"),
                "the first match is consumed, like an auto-acking RabbitMQ tap");
        });
    }

    [Test]
    public async Task ConcurrentAwaitsOnOneConsumer_ShouldConsumeEachMessageOnce()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging concurrent awaits", TestMethod());
        var broker = context.Service<IProtoMessageBroker>();
        var consumer = await broker.CreateConsumerAsync();
        context.RegisterResource(
            "messaging:consumer:concurrent",
            "consumer",
            "Concurrent consumer",
            _ => consumer.DisposeAsync());

        var first = consumer.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2)).AsTask();
        var second = consumer.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2)).AsTask();
        await Task.Delay(100);
        await context.Messaging().PublishAsync("invoices", "{\"id\":1}");
        await context.Messaging().PublishAsync("invoices", "{\"id\":2}");

        var received = await Task.WhenAll(first, second);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(received.Select(message => message.Payload),
            Is.EquivalentTo(new[] { "{\"id\":1}", "{\"id\":2}" }),
            "each message is consumed once per consumer; a concurrent await must not receive one another owns");
    }

    [Test]
    public async Task Await_ShouldNotTimeOutAfterAMatchWasAssigned()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging boundary", TestMethod());
        var broker = context.Service<IProtoMessageBroker>();
        var messages = context.Messaging();

        for (var index = 0; index < 50; index++)
        {
            var consumer = await broker.CreateConsumerAsync();
            var payload = $"{{\"id\":{index}}}";
            var awaited = consumer.AwaitAsync(
                "invoices",
                message => message.Payload == payload,
                TimeSpan.FromMilliseconds(1));
            await Task.Run(() => messages.PublishAsync("invoices", payload));

            try
            {
                var received = await awaited;
                Assert.That(received.Payload, Is.EqualTo(payload), "a won race returns the matching message");
            }
            catch (TimeoutException)
            {
                var recovered = await consumer.AwaitAsync(
                    "invoices",
                    message => message.Payload == payload,
                    TimeSpan.FromSeconds(2));
                Assert.That(recovered.Payload, Is.EqualTo(payload),
                    "a lost race must not consume the message already published at the timeout boundary");
            }

            await consumer.DisposeAsync();
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Broker_ShouldBeReleasedWithTheRunAndBackTheCapability()
    {
        var local = new ProtoHostBuilder();
        local.AddMessaging();
        await using var localHost = local.Build();
        Assert.That(localHost.HasCapability(ProtoCapabilityKinds.Broker), Is.False,
            "The in-memory default is a test double, not a broker capability.");

        var adapter = new FakeBroker();
        var withAdapter = new ProtoHostBuilder();
        withAdapter.AddMessaging(messaging => messaging.UseBroker(_ => adapter));
        var host = withAdapter.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);

        await host.StartAsync();
        var context = await host.StartTestAsync("messaging lifecycle", TestMethod());
        _ = context.Messaging();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(adapter.Disposed, Is.False);

        await host.DisposeAsync();
        Assert.That(adapter.Disposed, Is.True, "The broker is owned by the run and released with it.");
    }

    private sealed class FakeBroker : IProtoMessageBroker, IDisposable
    {
        public string Name => "Fake";

        public bool Disposed { get; private set; }

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

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
                => throw new TimeoutException("The fake broker never has messages.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static MethodInfo TestMethod()
        => typeof(MessagingTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
