namespace ProtoTest.Messaging;

using ProtoTest.Messaging.Internal;

/// <summary>
/// One test's view of a broker, created by <see cref="IProtoMessageBroker.CreateConsumerAsync"/> during
/// setup and disposed with the test's teardown. A consumer belongs to exactly one test: it must never be
/// shared, and its disposal removes whatever the adapter declared for the test (queues, in RabbitMQ's
/// case), so parallel tests on the same destination cannot steal each other's messages.
/// </summary>
public interface IProtoMessageConsumer : IAsyncDisposable
{
    /// <summary>
    /// Prepares the destinations this test intends to await before the system under test publishes, so a
    /// message published after this call is not missed. Adapters that keep history may treat it as a
    /// no-op.
    /// </summary>
    ValueTask PrepareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for the first message on <paramref name="destination"/> matching <paramref name="predicate"/>
    /// within <paramref name="timeout"/>. Only messages that reach this consumer count, so another test's
    /// traffic on a shared broker can never satisfy the await. Awaits on one consumer are serialized in
    /// call order, and a delivery that matches no active predicate is not consumed: it stays available to
    /// a later await on the same consumer, so concurrent awaits neither lose nor steal each other's
    /// messages and every matched message is consumed exactly once.
    /// </summary>
    ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits like <see cref="AwaitAsync(string, Func{ProtoMessage, bool}, TimeSpan, CancellationToken)"/>
    /// but matches only a delivery the transport carried under <paramref name="routingKey"/>; null
    /// matches any routing key and behaves exactly like the plain overload. The default implementation
    /// filters the predicate by <see cref="ProtoMessage.RoutingKey"/>, which a consumer over a broker
    /// that keeps history gets for free; a tap adapter overrides it to bind the routing key on its tap as
    /// well, so a direct exchange delivers the key this await matches.
    /// </summary>
    ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        string? routingKey,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        return AwaitAsync(
            destination,
            ProtoMessageRoutingKeys.Filter(predicate, routingKey),
            timeout,
            cancellationToken);
    }
}
