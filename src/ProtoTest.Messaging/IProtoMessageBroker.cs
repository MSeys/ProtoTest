namespace ProtoTest.Messaging;

using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoTest.Json;

/// <summary>
/// A message observed on or sent to a broker. Adapters map their technology's message onto this shape;
/// the test-side API and the trace never see a broker-specific type.
/// </summary>
public sealed record ProtoMessage(
    string Destination,
    string? Payload = null,
    IReadOnlyDictionary<string, string?>? Headers = null,
    string? ContentType = null)
{
    /// <summary>The assertions of this message, for example <c>Should.MatchShape(shape)</c>.</summary>
    [JsonIgnore]
    public ProtoMessageAssertions Should => new(this);

    /// <summary>
    /// Deserializes the payload as <typeparamref name="T"/>, or returns <c>default</c> for an empty
    /// payload. Uses <see cref="ProtoJsonDefaults.Reader"/> (case-insensitive property names) unless
    /// <paramref name="options"/> overrides it. A payload of the wrong shape throws
    /// <see cref="JsonException"/>; use <see cref="ReadRequired{T}(JsonSerializerOptions?)"/> when the
    /// payload must exist.
    /// </summary>
    public T? ReadAsJson<T>(JsonSerializerOptions? options = null)
        => ProtoJsonRead.Read<T>(Payload, jsonPath: null, required: false, ReadSemantics<T>(), options);

    /// <summary>
    /// Deserializes the payload as <typeparamref name="T"/> and fails when there is nothing to return:
    /// an empty payload or JSON <c>null</c> throws <see cref="MessagingAssertionException"/> naming the
    /// destination. The null check runs before the deserializer, so a value type reports the messaging
    /// assertion rather than a raw <see cref="JsonException"/>.
    /// </summary>
    public T ReadRequired<T>(JsonSerializerOptions? options = null)
        => ProtoJsonRead.Read<T>(Payload, jsonPath: null, required: true, ReadSemantics<T>(), options)!;

    /// <summary>
    /// Reads the value at <paramref name="jsonPath"/> as <typeparamref name="T"/> and fails when the
    /// payload is empty, the path does not resolve or the value is JSON <c>null</c>. The supported subset
    /// is <c>$</c>, dot members and <c>[n]</c> indices (see <see cref="JsonPathResolver"/>); a miss throws
    /// <see cref="MessagingAssertionException"/> naming the destination and the path.
    /// </summary>
    public T ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        return ProtoJsonRead.Read<T>(Payload, jsonPath, required: true, ReadSemantics<T>(), options)!;
    }

    // Messaging's half of the shared read: its exception type and destination subject. Messaging records
    // no deserialize trace event (the assertion exception is the evidence), so the sink stays null.
    private ProtoJsonReadSemantics ReadSemantics<T>() => new(
        RequiredFailure: message => new MessagingAssertionException($"{Destination} — {message}"),
        PathMissFailure: (message, inner) => new MessagingAssertionException($"{Destination} — {message}", inner),
        EmptyBodyReason: "the payload was empty",
        NullBodyReason: "the payload was JSON null");
}

/// <summary>
/// The broker capability's adapter contract. The capability owns the broker resource and the
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

    /// <summary>
    /// Declares each destination on the broker, so publishing to it and awaiting it both work even when
    /// nothing else declares it - a suite that owns the broker and publishes its own events. Called
    /// during test setup before any tap is prepared. Declaration is idempotent: a destination that
    /// already exists, declared earlier in the run or by the application with the same shape, is left
    /// as it is, and a repeated declaration is a no-op. An adapter whose broker has no topology
    /// implements this as a no-op, because every destination already exists there; the default
    /// implementation refuses instead of pretending, so an adapter that cannot create the destination
    /// fails the declaration loudly rather than letting the test publish into nothing.
    /// </summary>
    /// <exception cref="NotSupportedException">The adapter does not declare destinations.</exception>
    ValueTask DeclareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"The broker adapter '{Name}' does not declare destinations. Declare(...) needs an adapter " +
            "that creates the destination on the broker; remove the declaration or implement DeclareAsync.");
}
