namespace ProtoTest.Messaging.MassTransit;

using System.Text.Json;
using global::MassTransit;
using global::MassTransit.Metadata;
using global::MassTransit.Serialization;
using ProtoTest.Messaging;

/// <summary>
/// The MassTransit JSON envelope, for suites that talk to a broker directly instead of the
/// application's in-process test harness. <see cref="Wrap{T}(string, T, Guid?, Guid?, Guid?, IReadOnlyDictionary{string, string?}?)"/>
/// converts a contract instance (or, overloaded, a raw JSON payload) into the frame a MassTransit
/// consumer expects - the package's own envelope shape, camelCase, with the contract's
/// <c>urn:message:</c> URNs in <c>messageType</c> - and <see cref="Unwrap(ProtoMessage)"/> reads a
/// frame the bus published back into its metadata and contract. Both conversions are pure and work
/// with any <see cref="IProtoMessageBroker"/>; no bus, harness or server is needed.
/// </summary>
/// <remarks>
/// The body is MassTransit's envelope, not a hand-written mirror of it: the package's
/// <c>JsonMessageEnvelope</c> is serialized with the package's own serializer options, so the property
/// set, camelCase, the decimal-as-string rule and every optional field are the package's own. The
/// envelope path needs no transport headers - the <c>MT-*</c> headers belong to MassTransit's raw
/// serializer - so a frame's headers are the caller's own, carried both in the envelope's
/// <c>headers</c> and on the frame the broker publishes.
/// </remarks>
public static class MassTransitEnvelope
{
    /// <summary>
    /// The content type MassTransit's JSON envelope serializer declares on the wire and selects its
    /// deserializer by; a frame wrapped with <see cref="Wrap{T}(string, T, Guid?, Guid?, Guid?, IReadOnlyDictionary{string, string?}?)"/>
    /// carries it, and an await of a bus publish sees it.
    /// </summary>
    public const string ContentType = "application/vnd.masstransit+json";

    /// <summary>
    /// Wraps a contract instance into the frame a MassTransit consumer expects on
    /// <paramref name="destination"/>: the envelope body carries the package's <c>messageType</c> URNs
    /// for <typeparamref name="T"/> (its interfaces and base message types included, as the bus
    /// computes them), a generated <c>messageId</c>, <c>sentTime</c> and <c>conversationId</c> when the
    /// caller gives none, and the caller's headers. Publish the returned frame through
    /// <c>PublishAsync(frame.Destination, frame.Payload, frame.Headers, frame.ContentType)</c> or hand
    /// it to <see cref="IProtoMessageBroker.PublishAsync"/> directly.
    /// </summary>
    /// <param name="destination">
    /// The broker destination the frame goes to. On RabbitMQ this is the exchange MassTransit names
    /// after the contract - its entity name, for example <c>Billing:InvoicePaid</c> - which is what the
    /// acting bus's consumer endpoints bind their queues to.
    /// </param>
    /// <param name="message">The contract instance to publish; its runtime type is serialized.</param>
    /// <param name="messageId">The envelope's message id; a new one when omitted.</param>
    /// <param name="correlationId">The envelope's correlation id; none when omitted.</param>
    /// <param name="conversationId">The envelope's conversation id; a new one when omitted, the default the bus publish pipe uses.</param>
    /// <param name="headers">
    /// The caller's headers: they ride the frame's broker headers and the envelope's <c>headers</c>
    /// object, exactly as a bus publish context's headers do. Null values are dropped.
    /// </param>
    public static ProtoMessage Wrap<T>(
        string destination,
        T message,
        Guid? messageId = null,
        Guid? correlationId = null,
        Guid? conversationId = null,
        IReadOnlyDictionary<string, string?>? headers = null)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(message);
        if (!MessageTypeCache<T>.IsValidMessageType)
        {
            throw new ArgumentException(
                $"'{typeof(T)}' is not a valid MassTransit message type: {MessageTypeCache<T>.InvalidMessageTypeReason}",
                nameof(message));
        }

        return ToProtoMessage(
            destination,
            message,
            MessageTypeCache<T>.MessageTypeNames,
            messageId,
            correlationId,
            conversationId,
            headers,
            addresses: null);
    }

    /// <summary>
    /// Wraps a contract instance like <see cref="Wrap{T}(string, T, Guid?, Guid?, Guid?, IReadOnlyDictionary{string, string?}?)"/>
    /// and sets the request/response fields <paramref name="addresses"/> carries, so a consumer that
    /// replies through MassTransit's <c>RespondAsync</c> has the response address and request id it
    /// needs. A field left null stays unset, exactly as the command/event overload leaves them.
    /// </summary>
    /// <param name="destination">The broker destination; the contract's MassTransit entity name on RabbitMQ.</param>
    /// <param name="message">The contract instance to publish; its runtime type is serialized.</param>
    /// <param name="addresses">The source, destination and response addresses and the request id; null fields stay unset.</param>
    /// <param name="messageId">The envelope's message id; a new one when omitted.</param>
    /// <param name="correlationId">The envelope's correlation id; none when omitted.</param>
    /// <param name="conversationId">The envelope's conversation id; a new one when omitted.</param>
    /// <param name="headers">The caller's headers; null values are dropped.</param>
    public static ProtoMessage Wrap<T>(
        string destination,
        T message,
        MassTransitEnvelopeAddresses addresses,
        Guid? messageId = null,
        Guid? correlationId = null,
        Guid? conversationId = null,
        IReadOnlyDictionary<string, string?>? headers = null)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(addresses);
        if (!MessageTypeCache<T>.IsValidMessageType)
        {
            throw new ArgumentException(
                $"'{typeof(T)}' is not a valid MassTransit message type: {MessageTypeCache<T>.InvalidMessageTypeReason}",
                nameof(message));
        }

        return ToProtoMessage(
            destination,
            message,
            MessageTypeCache<T>.MessageTypeNames,
            messageId,
            correlationId,
            conversationId,
            headers,
            addresses);
    }

    /// <summary>
    /// Wraps an already-serialized contract payload the same way <see cref="Wrap{T}(string, T, Guid?, Guid?, Guid?, IReadOnlyDictionary{string, string?}?)"/>
    /// wraps an instance: the payload's JSON becomes the envelope's <c>message</c> (parsed and
    /// re-emitted by the same serializer the bus uses), and <paramref name="messageType"/> supplies the
    /// <c>messageType</c> URNs. Use it when the suite has the wire JSON rather than the contract
    /// instance.
    /// </summary>
    /// <param name="destination">The broker destination; the contract's MassTransit entity name on RabbitMQ.</param>
    /// <param name="payload">The contract payload as JSON.</param>
    /// <param name="messageType">The contract type the payload represents.</param>
    /// <param name="messageId">The envelope's message id; a new one when omitted.</param>
    /// <param name="correlationId">The envelope's correlation id; none when omitted.</param>
    /// <param name="conversationId">The envelope's conversation id; a new one when omitted.</param>
    /// <param name="headers">The caller's headers; null values are dropped.</param>
    public static ProtoMessage Wrap(
        string destination,
        string payload,
        Type messageType,
        Guid? messageId = null,
        Guid? correlationId = null,
        Guid? conversationId = null,
        IReadOnlyDictionary<string, string?>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(messageType);
        if (!MessageTypeCache.IsValidMessageType(messageType))
        {
            throw new ArgumentException(
                $"'{messageType}' is not a valid MassTransit message type: {MessageTypeCache.InvalidMessageTypeReason(messageType)}",
                nameof(messageType));
        }

        JsonElement element;
        try
        {
            using var document = JsonDocument.Parse(payload);
            element = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The payload for '{destination}' is not valid JSON for {messageType.Name}: {exception.Message}",
                exception);
        }

        return ToProtoMessage(
            destination,
            element,
            MessageTypeCache.GetMessageTypeNames(messageType),
            messageId,
            correlationId,
            conversationId,
            headers,
            addresses: null);
    }

    /// <summary>
    /// Wraps an already-serialized contract payload like
    /// <see cref="Wrap(string, string, Type, Guid?, Guid?, Guid?, IReadOnlyDictionary{string, string?}?)"/>
    /// and sets the request/response fields <paramref name="addresses"/> carries, so a consumer that
    /// replies through MassTransit's <c>RespondAsync</c> has the response address and request id it
    /// needs. A field left null stays unset.
    /// </summary>
    /// <param name="destination">The broker destination; the contract's MassTransit entity name on RabbitMQ.</param>
    /// <param name="payload">The contract payload as JSON.</param>
    /// <param name="messageType">The contract type the payload represents.</param>
    /// <param name="addresses">The source, destination and response addresses and the request id; null fields stay unset.</param>
    /// <param name="messageId">The envelope's message id; a new one when omitted.</param>
    /// <param name="correlationId">The envelope's correlation id; none when omitted.</param>
    /// <param name="conversationId">The envelope's conversation id; a new one when omitted.</param>
    /// <param name="headers">The caller's headers; null values are dropped.</param>
    public static ProtoMessage Wrap(
        string destination,
        string payload,
        Type messageType,
        MassTransitEnvelopeAddresses addresses,
        Guid? messageId = null,
        Guid? correlationId = null,
        Guid? conversationId = null,
        IReadOnlyDictionary<string, string?>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(addresses);
        if (!MessageTypeCache.IsValidMessageType(messageType))
        {
            throw new ArgumentException(
                $"'{messageType}' is not a valid MassTransit message type: {MessageTypeCache.InvalidMessageTypeReason(messageType)}",
                nameof(messageType));
        }

        JsonElement element;
        try
        {
            using var document = JsonDocument.Parse(payload);
            element = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The payload for '{destination}' is not valid JSON for {messageType.Name}: {exception.Message}",
                exception);
        }

        return ToProtoMessage(
            destination,
            element,
            MessageTypeCache.GetMessageTypeNames(messageType),
            messageId,
            correlationId,
            conversationId,
            headers,
            addresses);
    }

    /// <summary>
    /// Reads a frame as a MassTransit envelope: the <c>messageType</c> URNs, message id and the
    /// correlation and conversation ids, the sent time, and the raw <c>message</c> JSON. A frame that
    /// is not a MassTransit envelope fails naming the destination and the reason instead of returning
    /// empty metadata.
    /// </summary>
    /// <exception cref="MessagingAssertionException">The frame is not a MassTransit JSON envelope.</exception>
    public static MassTransitEnvelopeContent Unwrap(ProtoMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var envelope = ReadEnvelope(message);
        var payload = envelope.Message switch
        {
            JsonElement element => element.GetRawText(),
            null => throw NotAnEnvelope(message, "it carries no 'message'"),
            var value => JsonSerializer.Serialize(value, SystemTextJsonMessageSerializer.Options)
        };

        return new MassTransitEnvelopeContent(
            message.Destination,
            envelope.MessageType!,
            ParseId(message, "messageId", envelope.MessageId),
            ParseId(message, "correlationId", envelope.CorrelationId),
            ParseId(message, "conversationId", envelope.ConversationId),
            envelope.SentTime,
            payload)
        {
            SourceAddress = ParseAddress(message, "sourceAddress", envelope.SourceAddress),
            DestinationAddress = ParseAddress(message, "destinationAddress", envelope.DestinationAddress),
            ResponseAddress = ParseAddress(message, "responseAddress", envelope.ResponseAddress),
            RequestId = ParseId(message, "requestId", envelope.RequestId)
        };
    }

    /// <summary>
    /// Reads a frame as its contract: the frame must be a MassTransit envelope that declares
    /// <typeparamref name="T"/> (or one of its message URNs) in <c>messageType</c>, exactly like a
    /// consumer's type filter, and the envelope's <c>message</c> is deserialized with MassTransit's
    /// own serializer options. A frame that is not an envelope, or that does not carry
    /// <typeparamref name="T"/>, fails naming the destination.
    /// </summary>
    /// <exception cref="MessagingAssertionException">
    /// The frame is not a MassTransit JSON envelope, or its declared message types do not include
    /// <typeparamref name="T"/>, or its message is not valid JSON for <typeparamref name="T"/>.
    /// </exception>
    public static T Unwrap<T>(ProtoMessage message)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        var envelope = ReadEnvelope(message);
        var urn = MessageUrn.ForTypeString<T>();
        if (!envelope.MessageType!.Any(type => string.Equals(type, urn, StringComparison.OrdinalIgnoreCase)))
        {
            throw NotAnEnvelope(
                message,
                $"its messageType names {string.Join(", ", envelope.MessageType!)} rather than {urn}");
        }

        try
        {
            return envelope.Message switch
            {
                JsonElement element => element.Deserialize<T>(SystemTextJsonMessageSerializer.Options)
                    ?? throw NotAnEnvelope(message, $"its message for {urn} is JSON null"),
                null => throw NotAnEnvelope(message, "it carries no 'message'"),
                object value => (T)value
            };
        }
        catch (JsonException exception)
        {
            throw new MessagingAssertionException(
                $"{message.Destination} - not a MassTransit envelope: its message is not valid JSON for " +
                $"{typeof(T).Name}: {exception.Message}",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new MessagingAssertionException(
                $"{message.Destination} - not a MassTransit envelope: its message cannot be read as " +
                $"{typeof(T).Name}: {exception.Message}",
                exception);
        }
    }

    private static ProtoMessage ToProtoMessage(
        string destination,
        object message,
        string[] messageTypes,
        Guid? messageId,
        Guid? correlationId,
        Guid? conversationId,
        IReadOnlyDictionary<string, string?>? headers,
        MassTransitEnvelopeAddresses? addresses)
    {
        // The bus derives the sent time from the same NewId as a generated message id; mirror that, so
        // a wrapped frame's metadata matches a published one. A caller-provided id keeps its own time.
        var newId = NewId.Next();
        var messageHeaders = headers?
            .Where(header => header.Value is not null)
            .ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase);
        var envelope = new JsonMessageEnvelope
        {
            MessageId = (messageId ?? newId.ToGuid()).ToString("D"),
            RequestId = addresses?.RequestId?.ToString("D"),
            CorrelationId = correlationId?.ToString("D"),
            ConversationId = (conversationId ?? NewId.NextGuid()).ToString("D"),
            SourceAddress = addresses?.SourceAddress?.ToString(),
            DestinationAddress = addresses?.DestinationAddress?.ToString(),
            ResponseAddress = addresses?.ResponseAddress?.ToString(),
            MessageType = messageTypes,
            Message = message,
            SentTime = newId.Timestamp,
            Headers = messageHeaders is null
                ? []
                : messageHeaders.ToDictionary(header => header.Key, header => (object?)header.Value, StringComparer.OrdinalIgnoreCase),
            Host = HostMetadataCache.Host
        };

        string payload;
        try
        {
            payload = JsonSerializer.Serialize(envelope, SystemTextJsonMessageSerializer.Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"The message cannot be serialized into a MassTransit envelope: {exception.Message}",
                exception);
        }

        return new ProtoMessage(destination, payload, messageHeaders, ContentType);
    }

    // The envelope is read with MassTransit's own serializer options, and the fields the consumer
    // pipeline cannot run without - message and messageType - make a frame an envelope.
    private static JsonMessageEnvelope ReadEnvelope(ProtoMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw NotAnEnvelope(message, "the payload was empty");
        }

        JsonMessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<JsonMessageEnvelope>(message.Payload, SystemTextJsonMessageSerializer.Options);
        }
        catch (JsonException exception)
        {
            throw NotAnEnvelope(message, "the payload is not JSON", exception);
        }

        if (envelope is null)
        {
            throw NotAnEnvelope(message, "the payload was JSON null");
        }

        if (envelope.MessageType is not { Length: > 0 })
        {
            throw NotAnEnvelope(message, "it carries no 'messageType'");
        }

        if (envelope.Message is null)
        {
            throw NotAnEnvelope(message, "it carries no 'message'");
        }

        return envelope;
    }

    // MassTransit stores the ids as strings but its contexts require Guids; an id it could not read is
    // a frame it could not consume, named here instead of surfacing as a format error later.
    private static Guid? ParseId(ProtoMessage message, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var id)
            ? id
            : throw NotAnEnvelope(message, $"its '{name}' is not a GUID: {value}");
    }

    // An address the bus wrote is absolute (<c>rabbitmq://host/vhost/queue</c>); one that is not is a
    // frame a responder could not use, named here instead of surfacing when a reply is attempted.
    private static Uri? ParseAddress(ProtoMessage message, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var address)
            ? address
            : throw NotAnEnvelope(message, $"its '{name}' is not an absolute address: {value}");
    }

    private static MessagingAssertionException NotAnEnvelope(ProtoMessage message, string reason)
        => new($"{message.Destination} - not a MassTransit envelope: {reason}.");

    private static MessagingAssertionException NotAnEnvelope(ProtoMessage message, string reason, Exception inner)
        => new($"{message.Destination} - not a MassTransit envelope: {reason}: {inner.Message}", inner);
}

/// <summary>
/// A frame read as a MassTransit envelope by <see cref="MassTransitEnvelope.Unwrap(ProtoMessage)"/>:
/// the frame's destination, the declared <c>urn:message:</c> types, the ids and sent time the
/// envelope carries, and the raw <c>message</c> JSON for a contract read the caller does itself. The
/// request/response fields (<see cref="SourceAddress"/>, <see cref="DestinationAddress"/>,
/// <see cref="ResponseAddress"/>, <see cref="RequestId"/>) are null when the frame carries none.
/// </summary>
public sealed record MassTransitEnvelopeContent(
    string Destination,
    IReadOnlyList<string> MessageTypes,
    Guid? MessageId,
    Guid? CorrelationId,
    Guid? ConversationId,
    DateTime? SentTime,
    string Payload)
{
    /// <summary>The address the frame was sent from, when it carries one.</summary>
    public Uri? SourceAddress { get; init; }

    /// <summary>The address the frame was sent to, when it carries one.</summary>
    public Uri? DestinationAddress { get; init; }

    /// <summary>The address a response goes to, when the frame carries one.</summary>
    public Uri? ResponseAddress { get; init; }

    /// <summary>The request the frame belongs to, when the frame carries one.</summary>
    public Guid? RequestId { get; init; }
}

/// <summary>
/// The request/response fields a wrapped MassTransit frame can carry: the broker addresses the
/// transport used and the request id a responder replies under. Pass it to
/// <see cref="MassTransitEnvelope.Wrap{T}(string, T, MassTransitEnvelopeAddresses, Guid?, Guid?, Guid?, IReadOnlyDictionary{string, string?}?)"/>
/// for a consumer that replies through MassTransit's <c>RespondAsync</c>; a field left null stays
/// unset, exactly as a command/event frame leaves it.
/// </summary>
/// <param name="SourceAddress">The sending endpoint's address; none when omitted.</param>
/// <param name="DestinationAddress">The receiving endpoint's address; none when omitted.</param>
/// <param name="ResponseAddress">Where a response goes; a responder without one cannot reply.</param>
/// <param name="RequestId">The request the message belongs to, usually the request's message id.</param>
public sealed record MassTransitEnvelopeAddresses(
    Uri? SourceAddress = null,
    Uri? DestinationAddress = null,
    Uri? ResponseAddress = null,
    Guid? RequestId = null);
