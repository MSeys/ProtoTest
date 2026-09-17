namespace ProtoTest.Core.Internal;

/// <summary>Aggregates the normalized report items every collector and report source exposes.</summary>
internal static class ProtoReportItems
{
    public static IReadOnlyList<ProtoReportItem> Collect(
        IEnumerable<IProtoCollector> collectors,
        IEnumerable<IProtoReportSource> reportSources)
        => [.. collectors
            .OfType<IProtoReportSource>()
            .Concat(reportSources)
            .Distinct<IProtoReportSource>(ReferenceEqualityComparer.Instance)
            .SelectMany(source => source.GetReportItems())
            .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Identifier, StringComparer.OrdinalIgnoreCase)];
}
