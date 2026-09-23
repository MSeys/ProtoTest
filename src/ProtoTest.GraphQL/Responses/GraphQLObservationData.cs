namespace ProtoTest.GraphQL;

/// <summary>Response data attached to a <c>graphql.response</c> observation.</summary>
public sealed record GraphQLResponseData(
    string OperationType,
    string? OperationName,
    string Document,
    int HttpStatusCode,
    int ErrorCount,
    IReadOnlyList<string> ErrorCodes,
    TimeSpan Duration,
    string? VariablesJson = null);

/// <summary>
/// Failure data attached to a <c>graphql.failure</c> observation. The address and the message are
/// sanitized with the same rules as the response diagnostics, so credentials and sensitive values never
/// reach the trace.
/// </summary>
public sealed record GraphQLFailureData(
    string OperationType,
    string? OperationName,
    string? RequestUri,
    TimeSpan Duration,
    string ExceptionType,
    string Message,
    bool IsCanceled);

/// <summary>Shape-match data attached to a <c>graphql.contract.shape</c> observation.</summary>
internal sealed record GraphQLShapeMatchData(string RequestIdentifier, IReadOnlyList<string> MatchedProperties);


