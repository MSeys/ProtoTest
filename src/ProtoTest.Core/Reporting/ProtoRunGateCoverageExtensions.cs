namespace ProtoTest.Core;

/// <summary>
/// Coverage helpers for Run gates. Coverage is one kind of collected evidence among several, so its
/// arithmetic lives here rather than on <see cref="ProtoRunGateContext"/>.
/// </summary>
public static class ProtoRunGateCoverageExtensions
{
    /// <summary>
    /// Aggregates the collected coverage items per target and category. Items whose verdict is unknown
    /// (<see cref="ProtoReportItem.IsCovered"/> is <see langword="null"/>) are aggregates rather than
    /// units, so they are left out entirely, exactly as the report summary leaves them out.
    /// </summary>
    public static IReadOnlyList<ProtoCoverageSummary> CoverageSummaries(this ProtoRunGateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            .. context.Items.Flatten().CoverageUnits()
                .GroupBy(item => (item.TargetName, item.Category), CoverageKeyComparer.Instance)
                .Select(group =>
                {
                    var totals = group.CoverageTotals();
                    return new ProtoCoverageSummary(
                        group.Key.TargetName,
                        group.Key.Category,
                        totals.Covered,
                        totals.Total);
                })
                .OrderBy(summary => summary.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(summary => summary.Category, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Gets the coverage of one target, or <see langword="null"/> when it recorded none. A target may
    /// report several categories, so the result aggregates all of them rather than picking one.
    /// </summary>
    public static ProtoCoverageSummary? CoverageFor(this ProtoRunGateContext context, string targetName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        var categories = context.CoverageSummaries()
            .Where(summary => string.Equals(summary.TargetName, targetName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return categories.Length switch
        {
            0 => null,
            1 => categories[0],
            _ => new ProtoCoverageSummary(
                targetName,
                string.Join(", ", categories.Select(summary => summary.Category)),
                categories.Sum(summary => summary.Covered),
                categories.Sum(summary => summary.Total))
        };
    }

    /// <summary>Gets the coverage of one target in one category, or <see langword="null"/> when it recorded none.</summary>
    public static ProtoCoverageSummary? CoverageFor(
        this ProtoRunGateContext context,
        string targetName,
        string category)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return context.CoverageSummaries().FirstOrDefault(summary =>
            string.Equals(summary.TargetName, targetName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(summary.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Groups coverage as the lookups find it: target and category are compared ignoring case, so
    /// <c>api</c> and <c>API</c> are one row rather than two.
    /// </summary>
    private sealed class CoverageKeyComparer : IEqualityComparer<(string TargetName, string Category)>
    {
        public static readonly CoverageKeyComparer Instance = new();

        public bool Equals((string TargetName, string Category) left, (string TargetName, string Category) right)
            => string.Equals(left.TargetName, right.TargetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TargetName, string Category) key)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.TargetName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Category));
    }
}
