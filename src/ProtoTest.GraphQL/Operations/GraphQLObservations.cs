namespace ProtoTest.GraphQL;

using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;

/// <summary>
/// The observations a GraphQL response records, so a request and a subscription report the same shape
/// with the document redacted exactly once.
/// </summary>
internal static class GraphQLObservations
{
    /// <summary>The response observation both the request builder and the subscription record.</summary>
    public static ProtoObservation Response(
        string targetName,
        string identifier,
        GraphQLBuiltOperation operation,
        ProtoHttpAttachmentOptions? attachmentOptions,
        int statusCode,
        GraphQLResponse response,
        TimeSpan elapsed,
        string? variablesJson)
        => new(
            targetName,
            ProtoGraphQLBuilder.Protocol.ResponseObservationKind,
            identifier,
            new GraphQLResponseData(
                operation.Kind.WireName(),
                operation.Name,
                GraphQLDocumentRedactor.Redact(operation.DocumentText, attachmentOptions),
                statusCode,
                response.Errors.Count,
                response.Errors.Select(error => error.Code).Where(code => code is not null).Cast<string>().ToArray(),
                elapsed,
                variablesJson));
}
