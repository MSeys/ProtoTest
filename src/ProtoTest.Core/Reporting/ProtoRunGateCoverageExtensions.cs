namespace ProtoTest.Core;

/// <summary>
/// Coverage helpers for Run gates. Coverage is one kind of collected evidence among several, so its
/// arithmetic lives here rather than on <see cref="ProtoRunGateContext"/>.
/// </summary>
public static class ProtoRunGateCoverageExtensions
{
    /// <summary>Aggregates the collected coverage items per target and category.</summary>
    public static IReadOnlyList<ProtoCoverageSummary> CoverageSummaries(this ProtoRunGateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            .. context.ItemsOfKind(ProtoReportItemKinds.Coverage)
                .GroupBy(item => (item.TargetName, item.Category))
                .Select(group => new ProtoCoverageSummary(
                    group.Key.TargetName,
                    group.Key.Category,
                    group.Count(item => item.IsCovered == true),
                    group.Count()))
                .OrderBy(summary => summary.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(summary => summary.Category, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>Gets the coverage of one target, or <see langword="null"/> when it recorded none.</summary>
    public static ProtoCoverageSummary? CoverageFor(this ProtoRunGateContext context, string targetName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return context.CoverageSummaries().FirstOrDefault(summary =>
            string.Equals(summary.TargetName, targetName, StringComparison.OrdinalIgnoreCase));
    }
}
