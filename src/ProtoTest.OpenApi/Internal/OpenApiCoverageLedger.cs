namespace ProtoTest.OpenApi.Internal;

/// <summary>One endpoint hit: the method and the contract path the report shows.</summary>
internal readonly record struct OpenApiEndpointKey(string Method, string Route);

/// <summary>One response hit: the endpoint plus the response key the status code resolved to.</summary>
internal readonly record struct OpenApiResponseKey(string Method, string Route, string Response);

/// <summary>One property hit: the response plus the normalized schema property path.</summary>
internal readonly record struct OpenApiPropertyKey(string Method, string Route, string Response, string Property);

/// <summary>
/// The typed hit ledger behind the coverage report: one counter per endpoint, response and property,
/// keyed by the contract's own coordinates so a hit can never land on a row the report does not show.
/// The collector records under its lock; the report builder reads the counts through the accessors.
/// </summary>
internal sealed class OpenApiCoverageLedger
{
    private readonly Dictionary<OpenApiEndpointKey, int> _endpointHits = new();
    private readonly Dictionary<OpenApiResponseKey, int> _responseHits = new();
    private readonly Dictionary<OpenApiPropertyKey, int> _propertyHits = new();

    public void RecordEndpoint(string method, string route)
    {
        var key = new OpenApiEndpointKey(method, route);
        _endpointHits[key] = _endpointHits.GetValueOrDefault(key, 0) + 1;
    }

    public void RecordResponse(string method, string route, string response)
    {
        var key = new OpenApiResponseKey(method, route, response);
        _responseHits[key] = _responseHits.GetValueOrDefault(key, 0) + 1;
    }

    public void RecordProperty(string method, string route, string response, string propertyPath)
    {
        var key = new OpenApiPropertyKey(method, route, response, PropertyKey(propertyPath));
        _propertyHits[key] = _propertyHits.GetValueOrDefault(key, 0) + 1;
    }

    public int EndpointHits(string method, string route)
        => _endpointHits.GetValueOrDefault(new OpenApiEndpointKey(method, route), 0);

    public int ResponseHits(string method, string route, string response)
        => _responseHits.GetValueOrDefault(new OpenApiResponseKey(method, route, response), 0);

    public int PropertyHits(string method, string route, string response, string propertyPath)
        => _propertyHits.GetValueOrDefault(new OpenApiPropertyKey(method, route, response, PropertyKey(propertyPath)), 0);

    /// <summary>The endpoint hits recorded so far, as a snapshot: mutating the result must not change the ledger.</summary>
    public IReadOnlyDictionary<(string Method, string Route), int> EndpointSnapshot()
        => _endpointHits.ToDictionary(pair => (pair.Key.Method, pair.Key.Route), pair => pair.Value);

    /// <summary>
    /// The case-insensitive ledger key for a property path. A path is stored normalized because schema
    /// extraction is ignore-case, so a match reported as "$.Id" must land on the "$.id" baseline row.
    /// </summary>
    private static string PropertyKey(string path)
        => System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "[]").ToLowerInvariant();
}
