namespace ProtoTest.Messaging.Tests;

using ProtoTest.Core;

/// <summary>
/// The queue destination form against the in-memory broker: the form is the shared addressing rule,
/// and a broker with no queues refuses it with the named error instead of matching a destination that
/// only looks like a queue.
/// </summary>
[TestFixture]
public sealed class QueueDestinationTests
{
    [Test]
    public void Queue_ShouldBuildAndReadTheQueueForm()
    {
        var destination = ProtoDestination.Queue("billing.session-ended.dlq");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(destination, Is.EqualTo("queue:billing.session-ended.dlq"));
            Assert.That(ProtoDestination.IsQueue(destination), Is.True);
            Assert.That(ProtoDestination.QueueName(destination), Is.EqualTo("billing.session-ended.dlq"));
            Assert.That(ProtoDestination.IsQueue("csms.events"), Is.False, "a bare name is an exchange");
            Assert.That(ProtoDestination.IsQueue(null), Is.False);
            Assert.That(ProtoDestination.IsQueue(""), Is.False);
        }
    }

    [Test]
    public void Queue_ShouldRejectAnEmptyNameAndANonQueueDestination()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => ProtoDestination.Queue(" "));
            Assert.Throws<ArgumentException>(() => ProtoDestination.QueueName("csms.events"));
            Assert.Throws<ArgumentException>(() => ProtoDestination.QueueName("queue:"));
        }
    }

    [Test]
    public async Task Await_OnAQueueDestination_ShouldFailNamingTheInMemoryBroker()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("queue destination in-memory", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync("queue:orders", _ => true, TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        AssertQueueRefusal(error!, "queue:orders", "InMemory", "exchange");
    }

    [Test]
    public async Task Tap_OnAQueueDestination_ShouldFailTheAwaitNamingTheInMemoryBroker()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging(messaging => messaging.Tap("queue:orders"));
        await using var host = builder.Build();
        await host.StartAsync();

        // The setup hook prepares the tapped destination: the in-memory broker refuses the queue form
        // there, and the await that names it rethrows the recorded error instead of timing out.
        var context = await host.StartTestAsync("queue tap in-memory", TestMethods.Placeholder);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync("queue:orders", _ => true, TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        AssertQueueRefusal(error!, "queue:orders", "InMemory", "exchange");
    }

    [Test]
    public async Task Publish_OnAQueueDestination_ShouldFailNamingTheQueue()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("queue publish in-memory", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync("queue:orders", """{"id":1}"""));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("queue:orders"));
            Assert.That(error.Message, Does.Contain("publishes to exchanges"));
        }
    }

    [Test]
    public void Declare_OnAQueueDestination_ShouldFailNamingTheQueue()
    {
        var builder = new ProtoHostBuilder();

        var error = Assert.Throws<ArgumentException>(() =>
            builder.AddMessaging(messaging => messaging.Declare("queue:orders")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("queue:orders"));
            Assert.That(error.Message, Does.Contain("Declare creates the suite's exchanges"));
        }
    }

    private static void AssertQueueRefusal(
        InvalidOperationException error,
        string destination,
        string brokerName,
        string remedy)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Message, Does.Contain(destination));
            Assert.That(error.Message, Does.Contain(brokerName));
            Assert.That(error.Message, Does.Contain(remedy));
        }
    }
}
