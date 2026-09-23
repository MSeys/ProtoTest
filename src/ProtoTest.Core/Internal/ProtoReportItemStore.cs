namespace ProtoTest.Core.Internal;

/// <summary>
/// Run-scoped store for report items: one lock around a list with a snapshot reader, shared by the
/// finding store, the resource store and the gate verdict source so their thread-safety contract is a
/// single implementation. It outlives individual test contexts so their items reach the run's reports.
/// </summary>
internal class ProtoReportItemStore : IProtoReportSource
{
    private readonly ProtoLock _gate = new();
    private readonly List<ProtoReportItem> _items = [];

    public void Add(ProtoReportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            _items.Add(item);
        }
    }

    public IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }

    protected void AddRange(IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            _items.AddRange(items);
        }
    }

    /// <summary>Replaces the snapshot, for a source whose latest publication is its whole content.</summary>
    protected void Replace(IEnumerable<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            _items.Clear();
            _items.AddRange(items);
        }
    }
}
