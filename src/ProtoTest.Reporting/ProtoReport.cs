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
        var flattened = Flatten(roots).ToArray();
        var coverageItems = flattened.Where(item => item.Kind == ProtoReportItemKind.Coverage).ToArray();
        var covered = coverageItems.Count(item => item.IsCovered is true);

        return new ProtoReport(
            DateTimeOffset.UtcNow,
            new ProtoReportSummary(
                Total: flattened.Length,
                TotalOccurrences: flattened.Sum(item => item.Count),
                CoverageTotal: coverageItems.Length,
                Covered: covered,
                Uncovered: coverageItems.Length - covered,
                CoveragePercentage: coverageItems.Length == 0
                    ? 0
                    : Math.Round(covered * 100d / coverageItems.Length, 2),
                Warnings: flattened.Count(item => item.Status == ProtoReportStatus.Warning),
                Errors: flattened.Count(item => item.Status == ProtoReportStatus.Error)),
            roots);
    }

    private static IEnumerable<ProtoReportItem> Flatten(IEnumerable<ProtoReportItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.Children is not null)
            {
                foreach (var child in Flatten(item.Children))
                {
                    yield return child;
                }
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
    int Errors);
