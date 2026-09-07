namespace ProtoTest.Rest;

/// <summary>
/// Data payload sent when an HTTP request/response is recorded.
/// </summary>
public record RestHitData(
    string Method,
    string RouteTemplate,
    int StatusCode,
    string ResponseBody,
    IReadOnlyDictionary<string, string> Headers
);

/// <summary>
/// Data payload sent when a ShapeMatcher validation succeeds/runs.
/// </summary>
public record ShapeMatchData(
    string RouteTemplate,
    IReadOnlyList<string> MatchedProperties,
    Type TargetType
);