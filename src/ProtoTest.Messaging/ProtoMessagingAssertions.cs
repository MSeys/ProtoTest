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
    /// Matches a message payload against an expected shape and records the assertion on
    /// <paramref name="context"/> as an <c>assert.json.shape</c> operation with a
    /// <c>messaging.contract.shape</c> observation. A mismatch fails the operation and rethrows the
    /// shape exception; a payload that is empty or not Json fails with a message naming the destination.
    /// </summary>
    public static ProtoMessage ShouldMatchShape(
        this ProtoMessage message,
        ProtoExecutionContext context,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var targetName = context.TryService<IProtoMessageBroker>()?.Name ?? message.Destination;
        try
        {
            ProtoShapeAssertion.Assert(
                new ProtoShapeAssertionContext(
                    context,
                    "ProtoTest.Messaging",
                    "Assert message shape",
                    ExtraAttributes: new Dictionary<string, string?> { ["messaging.destination"] = message.Destination }),
                message.Payload,
                expectedShape,
                options,
                observation: matched => new ProtoObservation(
                    targetName,
                    "messaging.contract.shape",
                    message.Destination,
                    new MessagingShapeMatchData(message.Destination, matched)));
        }
        catch (JsonDocumentAssertionException exception)
        {
            throw new JsonDocumentAssertionException(
                $"The message on '{message.Destination}' was not valid Json: {exception.Message}",
                exception.Content,
                exception);
        }

        return message;
    }
}

/// <summary>Shape-match data attached to a <c>messaging.contract.shape</c> observation.</summary>
public sealed record MessagingShapeMatchData(string Destination, IReadOnlyList<string> MatchedProperties);
