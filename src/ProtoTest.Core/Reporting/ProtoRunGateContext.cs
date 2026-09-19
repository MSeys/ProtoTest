namespace ProtoTest.Core;

/// <summary>
/// The evidence a Run gate evaluates: everything the run's collectors and report sources produced,
/// whatever kind. Coverage, findings, metrics and observations all arrive as <see cref="ProtoReportItem"/>.
/// </summary>
public sealed class ProtoRunGateContext
{
    public ProtoRunGateContext(IReadOnlyList<ProtoReportItem> items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    /// <summary>Gets every item the run collected.</summary>
    public IReadOnlyList<ProtoReportItem> Items { get; }

    /// <summary>
    /// Returns the items of one kind, including nested items; <see cref="ProtoReportItemKinds"/> names
    /// the core kinds. Items are flattened exactly as coverage summaries flatten them.
    /// </summary>
    public IEnumerable<ProtoReportItem> ItemsOfKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return Items.Flatten().Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the items in one category, including nested items.</summary>
    public IEnumerable<ProtoReportItem> InCategory(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return Items.Flatten().Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the items of one target, including nested items.</summary>
    public IEnumerable<ProtoReportItem> ForTarget(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return Items.Flatten().Where(item => string.Equals(item.TargetName, targetName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the items with one status, including nested items.</summary>
    public IEnumerable<ProtoReportItem> WithStatus(ProtoReportStatus status)
        => Items.Flatten().Where(item => item.Status == status);
}
