namespace ProtoTest.Messaging;

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
/// The broker capability's state-free adapter contract. The capability owns the broker resource and the
/// trace-facing API; an adapter owns the client technology (RabbitMQ today, others later). One broker is
/// shared by the whole run and may publish concurrently, while every test gets its own
/// <see cref="IProtoMessageConsumer"/>.
/// </summary>
public interface IProtoMessageBroker
{
    /// <summary>The adapter name recorded on the capability and the broker entity.</summary>
    string Name { get; }

    /// <summary>Publishes one message to a destination.</summary>
    ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a consumer for one test. The caller owns it for that test and disposes it with the test's
    /// teardown, so an adapter can declare per-test taps and remove them at the test boundary: parallel
    /// tests on the same destination stay isolated instead of stealing each other's messages.
    /// </summary>
    ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default);
}
