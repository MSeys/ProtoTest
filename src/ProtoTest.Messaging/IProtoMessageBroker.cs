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
        => string.IsNullOrWhiteSpace(Payload)
            ? default
            : JsonSerializer.Deserialize<T>(Payload, options ?? ProtoJsonDefaults.Reader);

    /// <summary>
    /// Deserializes the payload as <typeparamref name="T"/> and fails when there is nothing to return:
    /// an empty payload or JSON <c>null</c> throws <see cref="MessagingAssertionException"/> naming the
    /// destination. The null check runs before the deserializer, so a value type reports the messaging
    /// assertion rather than a raw <see cref="JsonException"/>.
    /// </summary>
    public T ReadRequired<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(Payload))
        {
            throw RequiredFailure<T>(jsonPath: null, "the payload was empty");
        }

        using var document = JsonDocument.Parse(Payload);
        if (document.RootElement.ValueKind == JsonValueKind.Null)
        {
            throw RequiredFailure<T>(jsonPath: null, "the payload was JSON null");
        }

        var value = document.RootElement.Deserialize<T>(options ?? ProtoJsonDefaults.Reader);
        return value is null
            ? throw RequiredFailure<T>(jsonPath: null, "the payload was JSON null")
            : value;
    }

    /// <summary>
    /// Reads the value at <paramref name="jsonPath"/> as <typeparamref name="T"/> and fails when the
    /// payload is empty, the path does not resolve or the value is JSON <c>null</c>. The supported subset
    /// is <c>$</c>, dot members and <c>[n]</c> indices (see <see cref="JsonPathResolver"/>); a miss throws
    /// <see cref="MessagingAssertionException"/> naming the destination and the path.
    /// </summary>
    public T ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        if (string.IsNullOrWhiteSpace(Payload))
        {
            throw RequiredFailure<T>(jsonPath, "the payload was empty");
        }

        JsonElement element;
        try
        {
            using var document = JsonDocument.Parse(Payload);
            element = JsonPathResolver.Resolve(document.RootElement, jsonPath).Clone();
        }
        catch (JsonPathException exception)
        {
            throw new MessagingAssertionException($"{Destination} — {exception.Message}", exception);
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            throw RequiredFailure<T>(jsonPath, $"the value at '{jsonPath}' was JSON null");
        }

        var value = element.Deserialize<T>(options ?? ProtoJsonDefaults.Reader);
        return value is null
            ? throw RequiredFailure<T>(jsonPath, $"the value at '{jsonPath}' was JSON null")
            : value;
    }

    private MessagingAssertionException RequiredFailure<T>(string? jsonPath, string reason)
    {
        var read = string.IsNullOrWhiteSpace(jsonPath)
            ? $"ReadRequired<{typeof(T).Name}>"
            : $"ReadRequired<{typeof(T).Name}>('{jsonPath}')";
        return new MessagingAssertionException($"{Destination} — {read} failed: {reason}.");
    }
}

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
