namespace ProtoTest.Rest;

/// <summary>
/// Data payload sent when an HTTP request/response is recorded.
/// </summary>
public sealed record RestResponseData(
    string Method,
    string RouteTemplate,
    int StatusCode,
    string ResponseBody,
    IReadOnlyDictionary<string, string> Headers,
    string? RequestUri = null,
    TimeSpan? Duration = null
);

public sealed record RestFailureData(
    string Method,
    string RouteTemplate,
    string? RequestUri,
    TimeSpan Duration,
    string ExceptionType,
    string Message,
    bool IsCanceled);

/// <summary>
/// Data payload sent when a ShapeMatcher validation succeeds/runs.
/// </summary>
public sealed record RestShapeMatchData(
    string RequestIdentifier,
    IReadOnlyList<string> MatchedProperties,
    Type TargetType
);
