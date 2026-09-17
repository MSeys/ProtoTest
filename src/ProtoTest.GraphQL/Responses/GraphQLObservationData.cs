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

/// <summary>Failure data attached to a <c>graphql.failure</c> observation.</summary>
public sealed record GraphQLFailureData(
    string OperationType,
    string? OperationName,
    TimeSpan Duration,
    string ExceptionType,
    string Message);

/// <summary>Shape-match data attached to a <c>graphql.contract.shape</c> observation.</summary>
public sealed record GraphQLShapeMatchData(string RequestIdentifier, IReadOnlyList<string> MatchedProperties);
