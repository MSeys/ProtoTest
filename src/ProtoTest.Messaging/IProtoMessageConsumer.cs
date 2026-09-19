namespace ProtoTest.Messaging;

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
    /// traffic on a shared broker can never satisfy the await.
    /// </summary>
    ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
