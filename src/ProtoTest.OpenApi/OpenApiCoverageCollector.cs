namespace ProtoTest.OpenApi;

using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.OpenApi.Internal;
using ProtoTest.Rest;

/// <summary>
/// Collects OpenAPI coverage from REST responses and shape matches: the route matcher resolves each hit
/// to a contract path, the typed ledger counts it, and the report builder renders the contract's
/// endpoints, responses and properties whether or not they were hit.
/// </summary>
public sealed class OpenApiCoverageCollector : ProtoCoverageCollector
{
    private readonly OpenApiDocument _document;
    private readonly OpenApiRouteMatcher _routes;
    private readonly OpenApiCoverageLedger _ledger = new();

    public override string Category => "OpenAPI";

    public OpenApiCoverageCollector(
        string targetName,
        IConfiguration configuration,
        IEnumerable<ProtoApplicationTarget> applicationTargets)
        : base(targetName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var applicationName = ProtoApplicationTargets.ResolveApplication(targetName, applicationTargets);
        var application = ProtoApplication.Section(configuration, applicationName);
        var source = application["OpenApi:Specification"];

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                ProtoApplication.MissingSettingMessage(applicationName, "OpenApi:Specification"));
        }

        _document = OpenApiSpecLoader.Load(source, application["BaseUrl"]);
        _routes = new OpenApiRouteMatcher(_document);
    }

    public OpenApiCoverageCollector(string targetName, string openApiSpecSource)
        : base(targetName)
    {
        _document = OpenApiSpecLoader.Load(openApiSpecSource);
        _routes = new OpenApiRouteMatcher(_document);
    }

    public OpenApiCoverageCollector(string targetName, OpenApiDocument document)
        : base(targetName)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _routes = new OpenApiRouteMatcher(_document);
    }

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
            && observation.Data is RestResponseData or RestShapeMatchData;

    public override void Collect(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_lock)
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

    /// <summary>The endpoint hits recorded so far, keyed by method and matched route. A snapshot: mutating
    /// the returned dictionary must not change what the collector reports.</summary>
    internal IReadOnlyDictionary<(string Method, string Route), int> EndpointHits
        => _ledger.EndpointSnapshot();

    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_lock)
        {
            return OpenApiReportBuilder.Build(_document, _ledger, TargetName, Category);
        }
    }

    private void RecordRestHit(RestResponseData hit)
    {
        if (!TryResolve(hit.Method, hit.RouteTemplate, out var method, out var route, out var operation))
        {
            return;
        }

        _ledger.RecordEndpoint(method, route);
        if (OpenApiRouteMatcher.FindResponseKey(operation, hit.StatusCode) is { } responseKey)
        {
            _ledger.RecordResponse(method, route, responseKey);
        }
    }

    private void RecordShapeMatchHit(RestShapeMatchData hit)
    {
        var parts = hit.RequestIdentifier.Split(' ', 2);
        if (parts.Length < 2 || hit.StatusCode is null)
        {
            return;
        }

        if (!TryResolve(parts[0], parts[1], out var method, out var route, out var operation)
            || OpenApiRouteMatcher.FindResponseKey(operation, hit.StatusCode.Value) is not { } responseKey)
        {
            return;
        }

        foreach (var property in hit.MatchedProperties)
        {
            _ledger.RecordProperty(method, route, responseKey, property);
        }
    }

    /// <summary>
    /// Resolves a request method and route to a spec operation. Only a method the spec describes
    /// resolves: the report enumerates spec operations, so counting an unmatched method would record a
    /// hit nobody can see.
    /// </summary>
    private bool TryResolve(
        string requestMethod,
        string requestRoute,
        out string method,
        out string route,
        out OpenApiOperation operation)
    {
        method = requestMethod.ToUpperInvariant();
        route = string.Empty;
        operation = null!;
        if (_routes.Find(requestRoute) is not { } matchedRoute)
        {
            return false;
        }

        route = matchedRoute;
        return Enum.TryParse<OperationType>(method, ignoreCase: true, out var operationType)
            && _document.Paths.TryGetValue(route, out var pathItem)
            && pathItem.Operations.TryGetValue(operationType, out operation!);
    }
}
