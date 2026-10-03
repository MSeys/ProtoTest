namespace ProtoTest.Core;

/// <summary>
/// Traversal and coverage arithmetic shared by every consumer of <see cref="ProtoReportItem"/> trees -
/// the report summary, the HTML renderer and Run gate coverage - so the three cannot disagree.
/// </summary>
public static class ProtoReportItemExtensions
{
    /// <summary>Yields the item and all of its descendants, depth first.</summary>
    public static IEnumerable<ProtoReportItem> Flatten(this ProtoReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        yield return item;
        if (item.Children is null) yield break;
        foreach (var child in item.Children)
        {
            foreach (var descendant in child.Flatten())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Yields every item and all of its descendants, depth first.</summary>
    public static IEnumerable<ProtoReportItem> Flatten(this IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            foreach (var descendant in item.Flatten())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Tests an item's kind. Kinds are open strings and integrations may capitalize them, so the comparison ignores case.</summary>
    public static bool IsKind(this ProtoReportItem item, string kind)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The coverage items that carry a verdict. An item whose <see cref="ProtoReportItem.IsCovered"/> is
    /// <see langword="null"/> is an aggregate - a type, a grouping row - not a unit, so it is excluded
    /// from coverage accounting everywhere.
    /// </summary>
    public static IEnumerable<ProtoReportItem> CoverageUnits(this IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(item => item.IsKind(ProtoReportItemKinds.Coverage) && item.IsCovered is not null);
    }

    /// <summary>
    /// The coverage units with the path that names each one in its report: a nested unit's identifier
    /// under its parents' (<c>GET /a › 200 › $.id</c>), so units a collector names per parent stay apart.
    /// </summary>
    public static IEnumerable<ProtoCoverageUnitPath> CoverageUnitPaths(this IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.SelectMany(item => Paths(item, parent: null, parentPath: null));
    }

    private static IEnumerable<ProtoCoverageUnitPath> Paths(ProtoReportItem item, ProtoReportItem? parent, string? parentPath)
    {
        var path = ProtoCoverageUnitPath.Combine(parentPath, parent?.Identifier, item.Identifier);
        if (item.IsKind(ProtoReportItemKinds.Coverage) && item.IsCovered is not null)
        {
            yield return new ProtoCoverageUnitPath(item, path);
        }

        foreach (var child in item.Children ?? [])
        {
            foreach (var unit in Paths(child, item, path))
            {
                yield return unit;
            }
        }
    }

    /// <summary>Aggregates the coverage units in <paramref name="items"/>; aggregate rows are excluded from every count.</summary>
    public static ProtoCoverageTotals CoverageTotals(this IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var covered = 0;
        var uncovered = 0;
        foreach (var item in items.CoverageUnits())
        {
            if (item.IsCovered is true) covered++;
            else uncovered++;
        }

        return new ProtoCoverageTotals(covered + uncovered, covered);
    }
}

/// <summary>Coverage units counted across a set of report items: covered, uncovered and the rounded percentage.</summary>
public readonly record struct ProtoCoverageTotals(int Total, int Covered)
{
    public int Uncovered => Total - Covered;

    public double Ratio => Total == 0 ? 0d : (double)Covered / Total;

    /// <summary>The covered share, rounded to two decimals as the report summary prints it.</summary>
    public double Percentage => Math.Round(Ratio * 100d, 2);
}


/// <summary>A coverage unit and the path that names it in its report.</summary>
public readonly record struct ProtoCoverageUnitPath(ProtoReportItem Unit, string Path)
{
    /// <summary>Separates a nested unit's identifier from its parent's in a path.</summary>
    public const string Separator = " › ";

    /// <summary>
    /// A child's path: its identifier under its parent's path, or the identifier alone when the collector
    /// already qualified it with the parent's (<c>Query.users</c> under <c>Query</c>).
    /// </summary>
    public static string Combine(string? parentPath, string? parentIdentifier, string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(parentIdentifier))
        {
            return identifier;
        }

        return identifier.StartsWith(parentIdentifier, StringComparison.Ordinal)
            ? identifier
            : parentPath + Separator + identifier;
    }
}
