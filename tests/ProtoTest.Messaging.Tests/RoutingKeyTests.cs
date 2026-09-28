namespace ProtoTest.Messaging.Tests;

using ProtoTest.Core;

/// <summary>
/// The routing-key half of the messaging surface on the in-memory broker: a publish under a routing
/// key records it on the message, an await under one matches that key only, and an unmatched message
/// stays for the await that names its key. The RabbitMQ suite proves the same contract against a real
/// broker; the two brokers must not disagree about what a routing-key await means.
/// </summary>
[TestFixture]
public sealed class RoutingKeyTests
{
    [Test]
    public async Task PublishAndAwait_ShouldRoundTripByRoutingKey()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging routing key", TestMethods.Placeholder);
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "invoice.paid", "{\"id\":1}", contentType: "application/json");
        var received = await messages.AwaitAsync(
            "invoices",
            "invoice.paid",
            message => message.Payload == "{\"id\":1}",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received.Destination, Is.EqualTo("invoices"));
            Assert.That(received.RoutingKey, Is.EqualTo("invoice.paid"));
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
            Assert.That(
                entries.Single(entry => entry.Kind == "messaging.publish").Attributes["messaging.routing_key"],
                Is.EqualTo("invoice.paid"),
                "the publish operation names the routing key it used");
            Assert.That(
                entries.Single(entry => entry.Kind == "messaging.await").Attributes["messaging.routing_key"],
                Is.EqualTo("invoice.paid"),
                "the await operation names the routing key it asked for");
        }
    }

    [Test]
    public async Task Await_ByRoutingKey_ShouldSkipAnotherKeysMessageAndKeepItForItsOwnAwait()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging routing key isolation", TestMethods.Placeholder);
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "invoice.paid", "{\"id\":1}");
        await messages.PublishAsync("invoices", "invoice.shipped", "{\"id\":2}");

        var shipped = await messages.AwaitAsync("invoices", "invoice.shipped", _ => true, TimeSpan.FromSeconds(2));
        var paid = await messages.AwaitAsync("invoices", "invoice.paid", _ => true, TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shipped.Payload, Is.EqualTo("{\"id\":2}"));
            Assert.That(paid.Payload, Is.EqualTo("{\"id\":1}"),
                "the message under the other key was not consumed by the first await");
        }
    }

    [Test]
    public async Task Await_WithoutARoutingKey_ShouldMatchAMessagePublishedUnderOne()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging routing key plain await", TestMethods.Placeholder);
        var messages = context.Messaging();

        await messages.PublishAsync("invoices", "invoice.paid", "{\"id\":1}");
        var received = await messages.AwaitAsync(
            "invoices",
            message => message.Payload == "{\"id\":1}",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
            Assert.That(received.RoutingKey, Is.EqualTo("invoice.paid"), "the routing key is visible on the consumed message");
        });
    }

    [Test]
    public async Task Await_ByRoutingKey_ShouldTimeOutNamingTheDestinationAndKey()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging routing key timeout", TestMethods.Placeholder);
        var messages = context.Messaging();

        var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
            await messages.AwaitAsync("invoices", "invoice.missing", _ => true, TimeSpan.FromMilliseconds(50)));

        var failure = context.RecordedObservations.Single(item => item.Kind == "messaging.failure");
        await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeout!.Message, Does.Contain("invoices"));
            Assert.That(timeout.Message, Does.Contain("invoice.missing"));
            Assert.That(failure.Identifier, Is.EqualTo("invoices"));
            Assert.That(failure.Metadata!["messaging.routing_key"], Is.EqualTo("invoice.missing"),
                "the failure record names the routing key the test asked for");
        }
    }

    [Test]
    public async Task Publish_ByRoutingKey_ShouldRejectAnEmptyRoutingKey()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging routing key argument", TestMethods.Placeholder);
        var messages = context.Messaging();

        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await messages.PublishAsync("invoices", " ", "{\"id\":1}"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.ParamName, Is.EqualTo("routingKey"));
    }
}
