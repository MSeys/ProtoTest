namespace ProtoTest.OpenApi;

using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using ProtoTest.Core;
using ProtoTest.OpenApi.Internal;
using ProtoTest.Rest;

public class OpenApiCoverageCollector : ProtoCollector
{
    private readonly OpenApiDocument _document;
    private readonly Dictionary<(string Method, string Route), int> _endpointHits = new();
    private readonly Dictionary<(string Method, string Route, int Status), int> _statusCodeHits = new();
    private readonly Dictionary<(string Method, string Route, string PropertyPath), int> _propertyHits = new();

    public override string Category => "OpenAPI";

    public OpenApiCoverageCollector(string targetName, IConfiguration configuration)
        : base(targetName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clientConfiguration = configuration.GetSection($"ProtoTest:Clients:{targetName}");
        var source = clientConfiguration["OpenApi:Url"]
            ?? clientConfiguration["OpenApi:Path"]
            ?? clientConfiguration["OpenApi:Specification"];

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"No OpenAPI specification configured for target '{targetName}'. " +
                $"Set 'ProtoTest:Clients:{targetName}:OpenApi:Url', " +
                $"'ProtoTest:Clients:{targetName}:OpenApi:Path', or " +
                $"'ProtoTest:Clients:{targetName}:OpenApi:Specification'.");
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

    public override void RecordHit(CoverageHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        lock (Lock)
        {
            if (hit.Data is RestHitData restHit)
            {
                RecordRestHit(restHit);
            }
            else if (hit.Data is ShapeMatchData shapeHit)
            {
                RecordShapeMatchHit(shapeHit);
            }

            base.RecordHit(hit);
        }
    }

    private void RecordRestHit(RestHitData hit)
    {
        var matchedRoute = FindMatchingOpenApiRoute(hit.RouteTemplate);
        if (matchedRoute == null) return;

        var method = hit.Method.ToUpperInvariant();

        var epKey = (method, matchedRoute);
        _endpointHits[epKey] = _endpointHits.GetValueOrDefault(epKey, 0) + 1;

        var statusKey = (method, matchedRoute, hit.StatusCode);
        _statusCodeHits[statusKey] = _statusCodeHits.GetValueOrDefault(statusKey, 0) + 1;
    }

    private void RecordShapeMatchHit(ShapeMatchData hit)
    {
        var parts = hit.RouteTemplate.Split(' ', 2);
        if (parts.Length < 2) return;

        var method = parts[0].ToUpperInvariant();
        var matchedRoute = FindMatchingOpenApiRoute(parts[1]);
        if (matchedRoute == null) return;

        foreach (var prop in hit.MatchedProperties)
        {
            var propKey = (method, matchedRoute, prop);
            _propertyHits[propKey] = _propertyHits.GetValueOrDefault(propKey, 0) + 1;
        }
    }

    /// <summary>
    /// Generates hierarchical coverage items based on the loaded OpenAPI spec contract.
    /// Maps endpoints, response status codes, and schema properties to coverage nodes.
    /// </summary>
    public override IEnumerable<CoverageItem> GetReportItems()
    {
        lock (Lock)
        {
            var reportItems = new List<CoverageItem>();

            foreach (var (pathKey, pathItem) in _document.Paths)
            {
                foreach (var (operationType, operation) in pathItem.Operations)
                {
                    var method = operationType.ToString().ToUpperInvariant();
                    var endpointIdentifier = $"{method} {pathKey}";
                    var totalEndpointHits = _endpointHits.GetValueOrDefault((method, pathKey), 0);

                    var childItems = new List<CoverageItem>();

                    // 1. Status Codes Baseline
                    foreach (var (statusCodeKey, _) in operation.Responses)
                    {
                        if (int.TryParse(statusCodeKey, out var statusCode))
                        {
                            var statusHits = _statusCodeHits.GetValueOrDefault((method, pathKey, statusCode), 0);
                            childItems.Add(new CoverageItem(
                                TargetName: TargetName,
                                Category: "OpenAPI StatusCode",
                                Identifier: $"{endpointIdentifier} -> {statusCode}",
                                IsVisited: statusHits > 0,
                                HitCount: statusHits
                            ));
                        }
                    }

                    // 2. Schema Properties Baseline (defined in OpenAPI spec vs hit count)
                    var expectedProperties = OpenApiSchemaExtractor.ExtractResponseProperties(operation);

                    foreach (var propPath in expectedProperties)
                    {
                        var propHits = _propertyHits.GetValueOrDefault((method, pathKey, propPath), 0);

                        childItems.Add(new CoverageItem(
                            TargetName: TargetName,
                            Category: "OpenAPI Property",
                            Identifier: $"{endpointIdentifier} -> {propPath}",
                            IsVisited: propHits > 0,
                            HitCount: propHits
                        ));
                    }

                    // 3. Build Root Endpoint Item
                    var metadata = new Dictionary<string, object>
                    {
                        ["Method"] = method,
                        ["Route"] = pathKey,
                        ["Children"] = childItems
                    };

                    reportItems.Add(new CoverageItem(
                        TargetName: TargetName,
                        Category: Category,
                        Identifier: endpointIdentifier,
                        IsVisited: totalEndpointHits > 0,
                        HitCount: totalEndpointHits,
                        Metadata: metadata
                    ));
                }
            }

            return reportItems;
        }
    }

    private string? FindMatchingOpenApiRoute(string template)
    {
        var normalizedTemplate = NormalizeRoute(template);

        foreach (var pathKey in _document.Paths.Keys)
        {
            if (NormalizeRoute(pathKey).Equals(normalizedTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return pathKey;
            }
        }

        return null;
    }

    private static string NormalizeRoute(string route) => route.TrimEnd('/');
}