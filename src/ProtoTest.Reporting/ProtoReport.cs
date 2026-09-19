namespace ProtoTest.Reporting;

using ProtoTest.Core;

public sealed record ProtoReport(
    DateTimeOffset GeneratedAtUtc,
    ProtoReportSummary Summary,
    IReadOnlyList<ProtoReportItem> Items)
{
    public static ProtoReport Create(IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var roots = items.ToArray();
        var flattened = roots.Flatten().ToArray();
        // Coverage totals count units only; an IsCovered-null item is an aggregate, not a unit.
        var coverage = flattened.CoverageTotals();

        return new ProtoReport(
            DateTimeOffset.UtcNow,
            new ProtoReportSummary(
                Total: flattened.Length,
                // An occurrence is an observed fact: coverage hits and observations. A gate verdict or a
                // finding is recorded once, not observed repeatedly, so it does not inflate the count.
                // Hierarchical coverage contributes its top unit once; nested units are that unit's
                // breakdown, so one call is not counted at the endpoint, response and property levels.
                TotalOccurrences: CoverageOccurrences(roots)
                    + flattened
                        .Where(item => item.IsKind(ProtoReportItemKinds.Observation))
                        .Sum(item => item.Count),
                CoverageTotal: coverage.Total,
                Covered: coverage.Covered,
                Uncovered: coverage.Uncovered,
                CoveragePercentage: coverage.Percentage,
                Warnings: flattened.Count(item => item.Status == ProtoReportStatus.Warning),
                Errors: flattened.Count(item => item.Status == ProtoReportStatus.Error),
                Findings: flattened.Count(item => item.IsKind(ProtoReportItemKinds.Finding)),
                Gates: flattened.Count(item => item.IsKind(ProtoReportItemKinds.Gate)),
                Resources: flattened.Count(item => item.IsKind(ProtoReportItemKinds.Resource))),
            roots);
    }

    /// <summary>
    /// The observed coverage occurrences in a tree: each top-level coverage unit contributes its hit
    /// count once. Units nested under another unit are that unit's breakdown - an OpenAPI response and
    /// its properties describe the same calls the endpoint already counted - so they add nothing.
    /// Aggregate rows (a null <see cref="ProtoReportItem.IsCovered"/>) contribute nothing themselves
    /// and do not hide the units below them.
    /// </summary>
    private static int CoverageOccurrences(IEnumerable<ProtoReportItem> items)
    {
        var total = 0;
        foreach (var item in items)
        {
            Add(item, hasUnitAncestor: false);
        }

        return total;

        void Add(ProtoReportItem item, bool hasUnitAncestor)
        {
            var unit = item.IsKind(ProtoReportItemKinds.Coverage) && item.IsCovered is not null;
            if (unit && !hasUnitAncestor) total += item.Count;
            if (item.Children is null) return;
            foreach (var child in item.Children)
            {
                Add(child, hasUnitAncestor || unit);
            }
        }
    }
}

public sealed record ProtoReportSummary(
    int Total,
    int TotalOccurrences,
    int CoverageTotal,
    int Covered,
    int Uncovered,
    double CoveragePercentage,
    int Warnings,
    int Errors,
    int Findings,
    int Gates,
    int Resources);
