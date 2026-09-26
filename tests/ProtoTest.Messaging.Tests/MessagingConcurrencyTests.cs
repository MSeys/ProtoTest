namespace ProtoTest.Messaging.Tests;

using ProtoTest.Core;

/// <summary>
/// The in-memory half of the shared concurrency contract (see
/// <see cref="MessagingConcurrencyContract"/>): awaits on one consumer are serialized and a delivery
/// that matches no awaited predicate stays available to the waiter it belongs to instead of being
/// discarded by the racing one.
/// </summary>
[TestFixture]
public sealed class MessagingConcurrencyTests
{
    [Test]
    public async Task ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging concurrent contract", TestMethods.Placeholder);
        var broker = context.Service<IProtoMessageBroker>();

        await MessagingConcurrencyContract.ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages(
            broker,
            $"contract-{Guid.NewGuid():N}");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }
}
