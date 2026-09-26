namespace ProtoTest.TestSupport;

using ProtoTest.Messaging;

/// <summary>
/// The one concurrency contract every messaging adapter honors: awaits on one consumer are serialized
/// in call order, a delivery that matches no active predicate is not consumed and stays available to a
/// later await on that consumer, and every message is consumed exactly once. The orchestrator starts
/// the waiter that only accepts <c>strict</c> first, then publishes the delivery only the second waiter
/// accepts (<c>lenient</c>) before the one the first accepts, which is exactly the order an adapter that
/// discards a non-matching delivery loses. Shared by the in-memory and RabbitMQ suites, so both
/// adapters prove the same contract.
/// </summary>
public static class MessagingConcurrencyContract
{
    public static async Task ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages(
        IProtoMessageBroker broker,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        await using var consumer = await broker.CreateConsumerAsync();
        await consumer.PrepareAsync([destination]);

        var strict = consumer.AwaitAsync(
            destination,
            message => message.Payload == "strict",
            TimeSpan.FromSeconds(15)).AsTask();
        var lenient = consumer.AwaitAsync(
            destination,
            message => message.Payload == "lenient",
            TimeSpan.FromSeconds(15)).AsTask();

        await broker.PublishAsync(new ProtoMessage(destination, "lenient"));
        await broker.PublishAsync(new ProtoMessage(destination, "strict"));

        var received = await Task.WhenAll(strict, lenient);
        if (received[0].Payload != "strict" || received[1].Payload != "lenient")
        {
            throw new InvalidOperationException(
                $"The messaging concurrency contract was violated: the 'strict' await received " +
                $"'{received[0].Payload}' and the 'lenient' await received '{received[1].Payload}'. " +
                "Concurrent awaits on one consumer must neither lose nor steal each other's messages.");
        }
    }
}
