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
        var messages = context.Messages();

        await messages.PublishAsync("invoices", "{\"id\":1}", contentType: "application/json");
        var received = await messages.AwaitAsync(
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
        var messages = context.Messages();

        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await messages.AwaitAsync(_ => false, TimeSpan.FromMilliseconds(50)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        Assert.That(timeout!.Message, Does.Contain("InMemory"));
    }

    [Test]
    public async Task MessagesFromEarlierTests_ShouldNotSatisfyLaterAwaits()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();

        var first = await host.StartTestAsync("messaging first", TestMethod());
        await first.Messages().PublishAsync("invoices", "{\"id\":1}");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var second = await host.StartTestAsync("messaging second", TestMethod());
        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await second.Messages().AwaitAsync(
                message => message.Destination == "invoices",
                TimeSpan.FromMilliseconds(50)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        Assert.That(timeout!.Message, Does.Contain("InMemory"));
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
        _ = context.Messages();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(adapter.Disposed, Is.False);

        await host.DisposeAsync();
        Assert.That(adapter.Disposed, Is.True, "The broker is owned by the run and released with it.");
    }

    private sealed class FakeBroker : IProtoMessageBroker, IDisposable
    {
        public string Name => "Fake";

        public long Position => 0;

        public bool Disposed { get; private set; }

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<ProtoMessage> AwaitAsync(
            Func<ProtoMessage, bool> predicate,
            TimeSpan timeout,
            long afterPosition,
            CancellationToken cancellationToken = default)
            => throw new TimeoutException("The fake broker never has messages.");

        public void Dispose() => Disposed = true;
    }

    private static MethodInfo TestMethod()
        => typeof(MessagingTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
