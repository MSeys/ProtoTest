namespace ProtoTest.Messaging;

using ProtoTest.Core;

/// <summary>
/// A message observed on or sent to a broker. Adapters map their technology's message onto this shape;
/// the test-side API and the trace never see a broker-specific type.
/// </summary>
public sealed record ProtoMessage(
    string Destination,
    string? Payload = null,
    IReadOnlyDictionary<string, string?>? Headers = null,
    string? ContentType = null);

/// <summary>
/// The broker capability's adapter contract. The capability owns the broker resource and the
/// trace-facing API; an adapter owns the client technology (RabbitMQ today, others later).
/// </summary>
public interface IProtoMessageBroker
{
    /// <summary>The adapter name recorded on the capability and the broker entity.</summary>
    string Name { get; }

    /// <summary>
    /// A monotonic publish position. A per-test consumer snapshots it at setup and only ever matches
    /// messages published after that point, so one test can never consume another test's messages.
    /// </summary>
    long Position { get; }

    /// <summary>Publishes one message to a destination.</summary>
    ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default);

    /// <summary>Waits for the first message matching <paramref name="predicate"/> within the timeout.</summary>
    ValueTask<ProtoMessage> AwaitAsync(
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => AwaitAsync(predicate, timeout, afterPosition: 0, cancellationToken);

    /// <summary>
    /// Waits for the first message matching <paramref name="predicate"/> that was published after
    /// <paramref name="afterPosition"/>. This is the piece that makes event-driven and CQRS write sides
    /// testable without polling sleeps: the assertion is the wait, scoped to the test's own consumer.
    /// </summary>
    ValueTask<ProtoMessage> AwaitAsync(
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        long afterPosition,
        CancellationToken cancellationToken = default);
}
