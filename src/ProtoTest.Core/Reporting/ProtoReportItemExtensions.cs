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
