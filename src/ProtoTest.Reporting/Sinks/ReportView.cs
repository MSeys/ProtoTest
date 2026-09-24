namespace ProtoTest.Reporting;

using ProtoTest.Core;

/// <summary>How the report colours a coverage unit; the shared arithmetic in <see cref="ProtoReportItemExtensions"/> decides it.</summary>
internal enum ReportCoverageState
{
    NotApplicable,
    Covered,
    Partial,
    Uncovered
}

/// <summary>
/// One report item projected for rendering: the item plus the strings the markup derives from it and
/// its subtree, computed once. The projection walks the tree post-order, so a large report costs one
/// pass instead of re-flattening every subtree for every item.
/// </summary>
internal sealed record ReportItemView(
    ProtoReportItem Item,
    ReportCoverageState Coverage,
    string CoverageClass,
    string DisplayIdentifier,
    string ContextLabel,
    string StatusLabel,
    IReadOnlyList<ReportItemView> Children)
{
    public bool HasDetails
        => Item.Value is not null
            || !string.IsNullOrWhiteSpace(Item.Message)
            || Item.Tags is { Count: > 0 }
            || Item.Metadata is { Count: > 0 }
            || Children.Count > 0;
}

/// <summary>The one projection from report items to render views.</summary>
internal static class ReportViews
{
    /// <summary>Projects a tree of items; a root keeps its root context label and its children theirs.</summary>
    public static IReadOnlyList<ReportItemView> Project(IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. items.Select(item => Project(item, isRoot: true, out _, out _))];
    }

    private static ReportItemView Project(
        ProtoReportItem item,
        bool isRoot,
        out int covered,
        out int uncovered)
    {
        var unit = item.IsKind(ProtoReportItemKinds.Coverage) && item.IsCovered is not null;
        covered = unit && item.IsCovered is true ? 1 : 0;
        uncovered = unit && item.IsCovered is false ? 1 : 0;
        var children = new List<ReportItemView>(item.Children?.Count ?? 0);
        foreach (var child in item.Children ?? [])
        {
            children.Add(Project(child, isRoot: false, out var childCovered, out var childUncovered));
            covered += childCovered;
            uncovered += childUncovered;
        }

        var coverage = covered > 0 && uncovered > 0
            ? ReportCoverageState.Partial
            : covered > 0
                ? ReportCoverageState.Covered
                : uncovered > 0
                    ? ReportCoverageState.Uncovered
                    : ReportCoverageState.NotApplicable;
        return new ReportItemView(
            item,
            coverage,
            CoverageClass(coverage),
            item.DisplayName ?? item.Identifier,
            ContextLabel(item, isRoot),
            StatusLabel(item),
            children);
    }

    private static string CoverageClass(ReportCoverageState coverage)
        => coverage switch
        {
            ReportCoverageState.Covered => "covered",
            ReportCoverageState.Partial => "partial",
            ReportCoverageState.Uncovered => "uncovered",
            _ => "not-applicable"
        };

    /// <summary>
    /// Gates speak their own language: a gate does not succeed or error, it passes, advises or fails.
    /// </summary>
    private static string StatusLabel(ProtoReportItem item)
        => !item.IsKind(ProtoReportItemKinds.Gate)
            ? item.Status.ToString()
            : item.Status switch
            {
                ProtoReportStatus.Success => "Passed",
                ProtoReportStatus.Warning => "Warning",
                ProtoReportStatus.Error => "Failed",
                ProtoReportStatus.Info => "Info",
                _ => "Skipped"
            };

    private static string ContextLabel(ProtoReportItem item, bool isRoot)
    {
        if (item.IsKind(ProtoReportItemKinds.Gate))
        {
            return "Run gate";
        }

        if (item.IsKind(ProtoReportItemKinds.Finding))
        {
            return item.DisplayGroup is { Length: > 0 }
                ? $"{item.Category} · {item.DisplayGroup}"
                : item.Category;
        }

        var category = item.DisplayGroup ?? item.Category;
        return isRoot ? $"{item.TargetName} · {category}" : category;
    }
}
