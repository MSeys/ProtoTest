namespace ProtoTest.Messaging.MassTransit.Tests;

using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;

/// <summary>
/// The bridge's refusal of a queue destination: a MassTransit destination is a message contract type
/// and the bus owns its transport's topology, so the error names the contract-type address instead of
/// letting the await time out against a destination the bus cannot read.
/// </summary>
[TestFixture]
public sealed class MassTransitQueueDestinationTests
{
    [Test]
    public async Task Await_OnAQueueDestination_ShouldFailNamingTheMassTransitBroker()
    {
        await using var host = MassTransitSuite.Builder().Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit queue destination", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync("queue:orders", _ => true, TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        AssertRefusal(error!, "queue:orders");
    }

    [Test]
    public async Task Tap_OnAQueueDestination_ShouldFailTheAwaitNamingTheMassTransitBroker()
    {
        await using var host = MassTransitSuite.Builder(messaging => messaging
            .Tap("queue:orders")
            .UseMassTransit<Program>(MassTransitSuite.Application)).Build();
        await host.StartAsync();

        // The setup hook prepares the tapped destination: the bridge refuses the queue form there, and
        // the await that names it rethrows the recorded error instead of timing out.
        var context = await host.StartTestAsync("masstransit queue tap", TestMethods.Placeholder);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync("queue:orders", _ => true, TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        AssertRefusal(error!, "queue:orders");
    }

    private static void AssertRefusal(InvalidOperationException error, string destination)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Message, Does.Contain(destination));
            Assert.That(error.Message, Does.Contain("MassTransit"));
            Assert.That(error.Message, Does.Contain("message contract type"));
        }
    }
}
