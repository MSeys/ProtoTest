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

    /// <summary>Waits for the first message on a destination matching <paramref name="predicate"/> within the timeout.</summary>
    ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => AwaitAsync(destination, predicate, timeout, afterPosition: 0, cancellationToken);

    /// <summary>
    /// Waits for the first message on <paramref name="destination"/> matching <paramref name="predicate"/>
    /// that was published after <paramref name="afterPosition"/>. The destination is what an adapter taps
    /// (an exchange for RabbitMQ); the position scopes the wait to the test's own consumer, so a shared
    /// deployed broker cannot leak another test's messages into this one.
    /// </summary>
    ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        long afterPosition,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implemented by adapters whose consumers must exist before the system under test publishes: the tap
/// queue is declared and drained of earlier tests' backlog during setup, so a message published after
/// the test starts is never missed and never leaks between tests.
/// </summary>
public interface IProtoMessageBrokerSetup
{
    /// <summary>Prepares the destinations this test intends to await.</summary>
    void Prepare(IReadOnlyCollection<string> destinations);
}
