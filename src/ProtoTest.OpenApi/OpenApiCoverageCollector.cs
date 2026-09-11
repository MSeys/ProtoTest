namespace ProtoTest.OpenApi;

using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using ProtoTest.Core;
using ProtoTest.OpenApi.Internal;
using ProtoTest.Rest;

public class OpenApiCoverageCollector : ProtoCoverageCollector
{
    private readonly OpenApiDocument _document;
    private readonly Dictionary<(string Method, string Route), int> _endpointHits = new();
    private readonly Dictionary<(string Method, string Route, string Response), int> _responseHits = new();
    private readonly Dictionary<(string Method, string Route, string Response, string PropertyPath), int> _propertyHits = new();

    public override string Category => "OpenAPI";

    public OpenApiCoverageCollector(string targetName, IConfiguration configuration)
        : base(targetName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clientConfiguration = configuration.GetSection($"ProtoTest:Clients:{targetName}");
        var source = clientConfiguration["OpenApi:Specification"];

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"No OpenAPI specification configured for target '{targetName}'. " +
                $"Set 'ProtoTest:Clients:{targetName}:OpenApi:Specification'.");
        }

        _document = OpenApiSpecLoader.Load(source, clientConfiguration["BaseUrl"]);
    }

    public OpenApiCoverageCollector(string targetName, string openApiSpecSource)
        : base(targetName)
    {
        _document = OpenApiSpecLoader.Load(openApiSpecSource);
    }

    public OpenApiCoverageCollector(string targetName, OpenApiDocument document)
        : base(targetName)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public override void Collect(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (Lock)
        {
            if (observation.Data is RestResponseData restHit)
            {
                RecordRestHit(restHit);
            }
            else if (observation.Data is RestShapeMatchData shapeHit)
            {
                RecordShapeMatchHit(shapeHit);
            }
        }
    }

    private void RecordRestHit(RestResponseData hit)
    {
        var matchedRoute = FindMatchingOpenApiRoute(hit.RouteTemplate);
        if (matchedRoute == null) return;

        var method = hit.Method.ToUpperInvariant();
        var epKey = (method, matchedRoute);
        _endpointHits[epKey] = _endpointHits.GetValueOrDefault(epKey, 0) + 1;

        if (TryGetOperation(method, matchedRoute, out var operation)
            && FindResponseKey(operation, hit.StatusCode) is { } responseKey)
        {
            var statusKey = (method, matchedRoute, responseKey);
            _responseHits[statusKey] = _responseHits.GetValueOrDefault(statusKey, 0) + 1;
        }
    }

    private void RecordShapeMatchHit(RestShapeMatchData hit)
    {
        var parts = hit.RequestIdentifier.Split(' ', 2);
        if (parts.Length < 2) return;

        var method = parts[0].ToUpperInvariant();
        var matchedRoute = FindMatchingOpenApiRoute(parts[1]);
        if (matchedRoute == null
            || hit.StatusCode is null
            || !TryGetOperation(method, matchedRoute, out var operation)
            || FindResponseKey(operation, hit.StatusCode.Value) is not { } responseKey)
        {
            return;
        }

        foreach (var prop in hit.MatchedProperties)
        {
            var propKey = (method, matchedRoute, responseKey, NormalizePropertyPath(prop));
            _propertyHits[propKey] = _propertyHits.GetValueOrDefault(propKey, 0) + 1;
        }
    }

    /// <summary>
    /// Generates hierarchical coverage items based on the loaded OpenAPI spec contract.
    /// Maps endpoints, response status codes, and schema properties to coverage nodes.
    /// </summary>
    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (Lock)
        {
            var reportItems = new List<ProtoReportItem>();

            foreach (var (pathKey, pathItem) in _document.Paths)
            {
                foreach (var (operationType, operation) in pathItem.Operations)
                {
                    var method = operationType.ToString().ToUpperInvariant();
                    var endpointIdentifier = $"{method} {pathKey}";
                    var totalEndpointHits = _endpointHits.GetValueOrDefault((method, pathKey), 0);

                    var childItems = new List<ProtoReportItem>();

                    foreach (var (responseKey, response) in operation.Responses)
                    {
                        var responseHits = _responseHits.GetValueOrDefault((method, pathKey, responseKey), 0);
                        var propertyItems = OpenApiSchemaExtractor
                            .ExtractResponseProperties(_document, response)
                            .Select(propertyPath =>
                            {
                                var propertyHits = _propertyHits.GetValueOrDefault(
                                    (method, pathKey, responseKey, propertyPath), 0);
                                return new ProtoReportItem(
                                    TargetName: TargetName,
                                    Category: "OpenAPI Property",
                                    Identifier: propertyPath,
                                    Kind: ProtoReportItemKind.Coverage,
                                    Status: propertyHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                                    Count: propertyHits,
                                    IsCovered: propertyHits > 0);
                            })
                            .ToArray();

                        childItems.Add(new ProtoReportItem(
                            TargetName: TargetName,
                            Category: "OpenAPI Response",
                            Identifier: responseKey,
                            Kind: ProtoReportItemKind.Coverage,
                            Status: responseHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                            Count: responseHits,
                            IsCovered: responseHits > 0,
                            Children: propertyItems));
                    }
                    reportItems.Add(new ProtoReportItem(
                        TargetName: TargetName,
                        Category: Category,
                        Identifier: endpointIdentifier,
                        Kind: ProtoReportItemKind.Coverage,
                        Status: totalEndpointHits > 0 ? ProtoReportStatus.Success : ProtoReportStatus.Neutral,
                        Count: totalEndpointHits,
                        IsCovered: totalEndpointHits > 0,
                        Children: childItems
                    ));
                }
            }

            return reportItems;
        }
    }

    private string? FindMatchingOpenApiRoute(string template)
    {
        var normalizedTemplate = NormalizeRoute(template);
        return _document.Paths.Keys
            .Select(path => new { Path = path, Score = MatchRoute(normalizedTemplate, NormalizeRoute(path)) })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private bool TryGetOperation(string method, string route, out OpenApiOperation operation)
    {
        operation = null!;
        return Enum.TryParse<OperationType>(method, ignoreCase: true, out var operationType)
            && _document.Paths.TryGetValue(route, out var pathItem)
            && pathItem.Operations.TryGetValue(operationType, out operation!);
    }

    private static string? FindResponseKey(OpenApiOperation operation, int statusCode)
    {
        var exact = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (operation.Responses.ContainsKey(exact)) return exact;

        var wildcard = $"{statusCode / 100}XX";
        var wildcardKey = operation.Responses.Keys.FirstOrDefault(
            key => string.Equals(key, wildcard, StringComparison.OrdinalIgnoreCase));
        if (wildcardKey is not null) return wildcardKey;

        return operation.Responses.Keys.FirstOrDefault(
            key => string.Equals(key, "default", StringComparison.OrdinalIgnoreCase));
    }

    private static int MatchRoute(string requestRoute, string contractRoute)
    {
        if (string.Equals(requestRoute, contractRoute, StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        var requestSegments = requestRoute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var contractSegments = contractRoute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (requestSegments.Length != contractSegments.Length) return -1;

        var literalMatches = 0;
        for (var index = 0; index < requestSegments.Length; index++)
        {
            if (string.Equals(requestSegments[index], contractSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                literalMatches++;
                continue;
            }

            if (IsRouteParameter(contractSegments[index]))
            {
                continue;
            }

            if (IsRouteParameter(requestSegments[index]) || !string.Equals(
                    requestSegments[index],
                    contractSegments[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }
        }

        return literalMatches;
    }

    private static bool IsRouteParameter(string segment)
        => segment.Length > 2 && segment[0] == '{' && segment[^1] == '}';

    private static string NormalizeRoute(string route)
    {
        var path = Uri.TryCreate(route, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri.AbsolutePath
            : route.Split('#', 2)[0].Split('?', 2)[0];
        path = path.Trim();
        if (!path.StartsWith('/')) path = $"/{path}";
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static string NormalizePropertyPath(string path)
        => System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "[]");
}
