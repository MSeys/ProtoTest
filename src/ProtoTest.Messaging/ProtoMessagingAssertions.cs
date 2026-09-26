namespace ProtoTest.Messaging;

using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Shape assertions for consumed messages, reusing the Json shape matcher the REST, GraphQL and gRPC
/// integrations use. A message payload is Json by convention, so an expected anonymous shape reads the
/// same way as elsewhere.
/// </summary>
public static class ProtoMessagingAssertions
{
    /// <summary>
    /// Matches a message payload against an expected shape and records the assertion on the ambient
    /// <see cref="Proto.Context"/> as an <c>assert.json.shape</c> operation with a
    /// <c>messaging.contract.shape</c> observation. A mismatch fails the operation and rethrows the
    /// shape exception; a payload that is empty or not Json fails with a message naming the destination.
    /// </summary>
    /// <remarks>Obsolete: use <c>message.Should.MatchShape(shape)</c>.</remarks>
    [Obsolete("Use message.Should.MatchShape(shape) instead.")]
    public static ProtoMessage ShouldMatchShape(
        this ProtoMessage message,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Should.MatchShape(expectedShape, options);
    }
}

/// <summary>
/// The shape assertions of one consumed message, reached through <see cref="ProtoMessage.Should"/>.
/// Shape has no negated form, so the facade is positive-only. <see cref="MatchShape"/> returns the
/// message, so assertions chain.
/// </summary>
public sealed class ProtoMessageAssertions
{
    private readonly ProtoMessage _message;

    internal ProtoMessageAssertions(ProtoMessage message) => _message = message;

    /// <summary>
    /// Matches the payload against the expected shape through the shared matcher and records the
    /// assertion on the ambient <see cref="Proto.Context"/>. A mismatch - or an empty or non-Json
    /// payload - is rethrown as a <see cref="MessagingAssertionException"/> whose message starts with
    /// the destination, with the matcher exception as the inner exception. Returns the message.
    /// </summary>
    public ProtoMessage MatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        var context = Proto.Context;
        var targetName = context.TryService<IProtoMessageBroker>()?.Name ?? _message.Destination;
        try
        {
            ProtoShapeAssertion.Assert(
                new ProtoShapeAssertionContext(
                    context,
                    ProtoMessagingProtocol.Protocol.TraceSource,
                    "Assert message shape",
                    ExtraAttributes: new Dictionary<string, string?> { ["messaging.destination"] = _message.Destination }),
                _message.Payload,
                expectedShape,
                options,
                observation: matched => new ProtoObservation(
                    targetName,
                    ProtoMessagingProtocol.ShapeObservationKind,
                    _message.Destination,
                    new MessagingShapeMatchData(_message.Destination, matched)));
        }
        catch (JsonShapeMismatchException exception)
        {
            throw new MessagingAssertionException($"{_message.Destination} — {exception.Message}", exception);
        }
        catch (JsonDocumentAssertionException exception)
        {
            throw new MessagingAssertionException($"{_message.Destination} — {exception.Message}", exception);
        }

        return _message;
    }
}

/// <summary>Shape-match data attached to a <c>messaging.contract.shape</c> observation.</summary>
public sealed record MessagingShapeMatchData(string Destination, IReadOnlyList<string> MatchedProperties);

/// <summary>
/// Represents a failed assertion on a consumed message; the message names the destination it was made
/// against. A shape failure keeps the shared matcher exception as its inner exception, so the
/// mismatch list stays inspectable.
/// </summary>
public sealed class MessagingAssertionException : ProtoAssertionException
{
    public MessagingAssertionException(string message)
        : base(message)
    {
    }

    public MessagingAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
