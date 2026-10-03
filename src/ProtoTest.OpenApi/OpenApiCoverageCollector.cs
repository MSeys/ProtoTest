namespace ProtoTest.OpenApi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.OpenApi.Internal;
using ProtoTest.OpenApi.Internal.Model;
using ProtoTest.Rest;

/// <summary>
/// Collects OpenAPI coverage from REST responses and shape matches: the route matcher resolves each hit
/// to a contract path, the typed ledger counts it, and the report builder renders the contract's
/// endpoints, responses and properties whether or not they were hit. A source that is a path on the
/// application (<c>/openapi/v1.json</c>) with no address to resolve it against loads from the application
/// once the run's infrastructure started, so an in-process application documents itself.
/// </summary>
public sealed class OpenApiCoverageCollector : ProtoCoverageCollector, IProtoRunHook
{
    private static readonly OpenApiSpec s_empty = new(new Dictionary<string, OpenApiSpecPath>());

    private OpenApiSpec _document = s_empty;
    private OpenApiRouteMatcher _routes = new(s_empty);
    private readonly OpenApiCoverageLedger _ledger = new();
    private IReadOnlyDictionary<string, object>? _specIdentity;
    private DeferredSource? _deferred;

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

        if (ProtoDocumentSource.IsApplicationPath(source, application["BaseUrl"]))
        {
            _deferred = new DeferredSource(source, applicationName);
            return;
        }

        Use(source, OpenApiSpecLoader.LoadWithContent(source, application["BaseUrl"]));
    }

    public OpenApiCoverageCollector(string targetName, string openApiSpecSource)
        : base(targetName)
    {
        if (ProtoDocumentSource.IsApplicationPath(openApiSpecSource))
        {
            _deferred = new DeferredSource(openApiSpecSource, ApplicationName: null);
            return;
        }

        Use(openApiSpecSource, OpenApiSpecLoader.LoadWithContent(openApiSpecSource));
    }

    /// <summary>Loads a document the application serves, now that the run can reach the application.</summary>
    async Task IProtoRunHook.AfterInfrastructureAsync(ProtoRunSetupContext context)
    {
        if (_deferred is not { } deferred)
        {
            return;
        }

        var application = deferred.ApplicationName
            ?? ProtoApplicationTargets.ResolveApplication(TargetName, context.Services.GetServices<ProtoApplicationTarget>());
        var client = await context.ApplicationClientAsync(application);
        var content = await ProtoDocumentSource.LoadTextAsync(deferred.Source, client, context.CancellationToken);
        lock (_lock)
        {
            Use(deferred.Source, (content, OpenApiSpecLoader.Parse(content)));
            _deferred = null;
        }
    }

    private void Use(string source, (string Content, OpenApiSpec Document) loaded)
    {
        _document = loaded.Document;
        _routes = new OpenApiRouteMatcher(_document);
        _specIdentity = ProtoSpecIdentity.Metadata(source, loaded.Content);
    }

    private sealed record DeferredSource(string Source, string? ApplicationName);

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
            var items = OpenApiReportBuilder.Build(_document, _ledger, TargetName, Category);
            return _specIdentity is null ? items : [SpecIdentityItem(), .. items];
        }
    }

    /// <summary>The aggregate item carrying the loaded document's identity. It has no verdict, so the
    /// coverage arithmetic and every existing report row stay untouched.</summary>
    private ProtoReportItem SpecIdentityItem() => new(
        TargetName,
        Category,
        ProtoSpecIdentity.ReportIdentifier,
        Kind: ProtoReportItemKinds.Coverage,
        Status: ProtoReportStatus.Neutral,
        IsCovered: null,
        Metadata: _specIdentity,
        DisplayName: "Specification");

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
        // The structured method and route are the contract; the display identifier is not parsed.
        if (hit.Method is null || hit.RouteTemplate is null || hit.StatusCode is null)
        {
            return;
        }

        if (!TryResolve(hit.Method, hit.RouteTemplate, out var method, out var route, out var operation)
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
        out OpenApiSpecOperation operation)
    {
        method = requestMethod.ToUpperInvariant();
        route = string.Empty;
        operation = null!;
        if (_routes.Find(requestRoute) is not { } matchedRoute)
        {
            return false;
        }

        route = matchedRoute;
        return _document.Paths.TryGetValue(route, out var pathItem)
            && pathItem.Operations.TryGetValue(method, out operation!);
    }
}
