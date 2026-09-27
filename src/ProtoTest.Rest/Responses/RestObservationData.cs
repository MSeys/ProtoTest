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

/// <summary>
/// Data payload sent when an HTTP request fails.
/// </summary>
/// <param name="ExceptionType">
/// The exception that produced the failure, or a producer-defined marker when the failure is
/// synthetic: WireMock.Net records an unmatched request without an exception, so the fake reports
/// <c>WireMockUnmatchedRequest</c> instead of inventing a type.
/// </param>
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
    Type TargetType,
    int? StatusCode = null
)
{
    /// <summary>
    /// The HTTP method the shape was matched on, for example <c>GET</c>, when the producer knows it.
    /// Structured routing data: consumers must not recover the method from
    /// <see cref="RequestIdentifier"/>, which is the display form.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// The route template the shape was matched on, for example <c>/orders/{id}</c>, when the producer
    /// knows it. Structured routing data; see <see cref="Method"/>.
    /// </summary>
    public string? RouteTemplate { get; init; }
}
