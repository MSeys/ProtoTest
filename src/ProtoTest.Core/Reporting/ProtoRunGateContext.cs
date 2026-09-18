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

    /// <summary>Returns the items of one kind; <see cref="ProtoReportItemKinds"/> names the core kinds.</summary>
    public IEnumerable<ProtoReportItem> ItemsOfKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return Items.Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<ProtoReportItem> InCategory(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return Items.Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<ProtoReportItem> ForTarget(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return Items.Where(item => string.Equals(item.TargetName, targetName, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<ProtoReportItem> WithStatus(ProtoReportStatus status)
        => Items.Where(item => item.Status == status);
}
