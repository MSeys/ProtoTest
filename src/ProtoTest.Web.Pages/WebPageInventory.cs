namespace ProtoTest.Web.Pages;

using ProtoTest.Core;

/// <summary>
/// Records page inventory the one way every producer records it: each route is normalized to its page
/// pattern and emitted as a <c>web.page.available</c> observation, so explicit configuration, the source
/// scanner, Vue runtime discovery and the ASP.NET Core endpoint inventory land on the same identities.
/// </summary>
public static class WebPageInventory
{
    /// <summary>The observation kind an available page is recorded under; every producer uses it.</summary>
    public const string AvailableObservationKind = "web.page.available";

    /// <summary>The observation kind a page the session opened is recorded under; it is evidence, not coverage.</summary>
    public const string VisitedObservationKind = "web.page.visited";

    /// <summary>The observation kind a passing page assertion is recorded under; only this kind marks a page covered.</summary>
    public const string VerifiedObservationKind = "web.page.verified";

    /// <summary>The metadata key naming the producer that recorded an inventory entry.</summary>
    public const string SourceMetadataKey = "web.page.source";

    /// <summary>
    /// Normalizes and records the routes, skipping duplicates and blank entries; returns how many were
    /// recorded. <paramref name="metadata"/> carries producer-specific facts (session, application) and
    /// is copied, never mutated.
    /// </summary>
    public static int Record(
        ProtoExecutionContext context,
        string targetName,
        IEnumerable<string> routes,
        string source,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var recorded = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            var path = WebPagePath.NormalizeRoute(route);
            if (path is null || !seen.Add(path))
            {
                continue;
            }

            var observationMetadata = metadata is null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(metadata);
            observationMetadata[SourceMetadataKey] = source;
            context.RecordObservation(new ProtoObservation(
                targetName,
                AvailableObservationKind,
                path,
                Metadata: observationMetadata));
            recorded++;
        }

        return recorded;
    }
}
