namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Reports the fields that arrived in REST responses but that no shape assertion mentioned, as their
/// own report section. Observed fields are never counted as covered: the section protects the
/// assertion-level coverage rule and shows the fields a passing suite never checked. Register it on a
/// REST target (<c>AddCollector&lt;RestTrafficCoverageCollector&gt;()</c>) to opt in.
/// </summary>
/// <remarks>
/// The collector consumes the run's response and shape observations. Response bodies are the sanitized
/// bodies the trace already carries, so a body truncated by the diagnostic cap cannot be analyzed and
/// contributes nothing; a shape assertion made without a context records no structured route and is
/// ignored. Fields are compared per method, route template and status code, and a field any shape
/// mentioned counts as asserted for the run.
/// </remarks>
public sealed class RestTrafficCoverageCollector(string targetName)
    : ProtoCoverageCollector(targetName)
{
    private readonly Dictionary<TrafficRouteKey, TrafficRoute> _routes = new();

    public override string Category => "REST traffic";

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
            && observation.Data is RestResponseData or RestShapeMatchData;

    public override void Collect(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        switch (observation.Data)
        {
            case RestResponseData response:
                RecordResponse(response);
                break;
            case RestShapeMatchData shape:
                RecordShape(shape);
                break;
        }
    }

    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_lock)
        {
            return
            [
                .. _routes
                    .OrderBy(entry => entry.Key.Method, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.RouteTemplate, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.StatusCode)
                    .Select(entry => BuildItem(entry.Key, entry.Value))
                    .Where(item => item is not null)
                    .Select(item => item!)
            ];
        }
    }

    private void RecordResponse(RestResponseData response)
    {
        // Only a JSON body has fields; anything else has nothing to report as observed.
        if (!JsonDiagnosticSanitizer.LooksLikeJson(response.ResponseBody)) return;
        lock (_lock)
        {
            Route(new TrafficRouteKey(response.Method.ToUpperInvariant(), response.RouteTemplate, response.StatusCode))
                .Bodies.Add(response.ResponseBody);
        }
    }

    private void RecordShape(RestShapeMatchData shape)
    {
        // The structured method and route are the contract; a shape observation without them (an
        // untraced assertion) has no route to report under.
        if (shape.Method is null || shape.RouteTemplate is null || shape.StatusCode is null) return;
        lock (_lock)
        {
            Route(new TrafficRouteKey(shape.Method.ToUpperInvariant(), shape.RouteTemplate, shape.StatusCode.Value))
                .Mentioned.UnionWith(shape.MatchedProperties);
        }
    }

    private TrafficRoute Route(TrafficRouteKey key)
    {
        if (!_routes.TryGetValue(key, out var route))
        {
            route = new TrafficRoute();
            _routes[key] = route;
        }

        return route;
    }

    private ProtoReportItem? BuildItem(TrafficRouteKey key, TrafficRoute route)
    {
        var unmentioned = new List<JsonUnmentionedField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var body in route.Bodies)
        {
            IReadOnlyList<JsonUnmentionedField> fields;
            try
            {
                fields = JsonShapeMatcher.FindUnmentionedFields(body, route.Mentioned);
            }
            catch (JsonDocumentAssertionException)
            {
                // A body the diagnostic cap truncated is not valid JSON; it cannot be analyzed and is
                // not reported as a gap.
                continue;
            }

            foreach (var field in fields)
            {
                if (seen.Add(field.PropertyPath))
                {
                    unmentioned.Add(field);
                }
            }
        }

        if (unmentioned.Count == 0)
        {
            return null;
        }

        return new ProtoReportItem(
            TargetName,
            Category,
            $"{key.Method} {key.RouteTemplate} · {key.StatusCode}",
            Kind: ProtoReportItemKinds.Traffic,
            Status: ProtoReportStatus.Neutral,
            IsCovered: null,
            Message: "Fields that arrived in a response but no shape assertion mentioned.",
            Children: [.. unmentioned.Select(field => new ProtoReportItem(
                TargetName,
                Category,
                field.PropertyPath,
                Kind: ProtoReportItemKinds.Traffic,
                Status: ProtoReportStatus.Neutral,
                IsCovered: false))]);
    }

    private readonly record struct TrafficRouteKey(string Method, string RouteTemplate, int StatusCode);

    private sealed class TrafficRoute
    {
        /// <summary>The distinct response bodies observed for the route, sanitized as the trace carries them.</summary>
        public HashSet<string> Bodies { get; } = new(StringComparer.Ordinal);

        /// <summary>Every property path a shape assertion matched for the route.</summary>
        public HashSet<string> Mentioned { get; } = new(StringComparer.Ordinal);
    }
}
