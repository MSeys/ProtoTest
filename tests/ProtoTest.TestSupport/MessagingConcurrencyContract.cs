namespace ProtoTest.TestSupport;

using ProtoTest.Messaging;

/// <summary>
/// The one concurrency contract every messaging adapter honors: awaits on one consumer are serialized
/// in call order, a delivery that matches no active predicate is not consumed and stays available to a
/// later await on that consumer, and every message is consumed exactly once. The orchestrator starts
/// the waiter that only accepts the strict payload first, then publishes the delivery only the second
/// waiter accepts before the one the first accepts, which is exactly the order an adapter that discards
/// a non-matching delivery loses. Shared by the in-memory, RabbitMQ and MassTransit suites, so every
/// adapter proves the same contract; a broker whose messages are typed contracts passes JSON payloads
/// for its contract and the same plain strings otherwise.
/// </summary>
public static class MessagingConcurrencyContract
{
    public static async Task ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages(
        IProtoMessageBroker broker,
        string destination,
        string strictPayload = "strict",
        string lenientPayload = "lenient")
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(strictPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(lenientPayload);

        await using var consumer = await broker.CreateConsumerAsync();
        await consumer.PrepareAsync([destination]);

        var strict = consumer.AwaitAsync(
            destination,
            message => message.Payload == strictPayload,
            TimeSpan.FromSeconds(15)).AsTask();
        var lenient = consumer.AwaitAsync(
            destination,
            message => message.Payload == lenientPayload,
            TimeSpan.FromSeconds(15)).AsTask();

        await broker.PublishAsync(new ProtoMessage(destination, lenientPayload));
        await broker.PublishAsync(new ProtoMessage(destination, strictPayload));

        var received = await Task.WhenAll(strict, lenient);
        if (received[0].Payload != strictPayload || received[1].Payload != lenientPayload)
        {
            throw new InvalidOperationException(
                $"The messaging concurrency contract was violated: the 'strict' await received " +
                $"'{received[0].Payload}' and the 'lenient' await received '{received[1].Payload}'. " +
                "Concurrent awaits on one consumer must neither lose nor steal each other's messages.");
        }
    }
}
