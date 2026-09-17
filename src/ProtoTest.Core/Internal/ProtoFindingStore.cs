namespace ProtoTest.Core.Internal;

/// <summary>
/// Run-scoped store for findings recorded by tests and hooks. It outlives individual test contexts so
/// findings reach the run's reports and run gates.
/// </summary>
internal sealed class ProtoFindingStore : IProtoReportSource
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
}
