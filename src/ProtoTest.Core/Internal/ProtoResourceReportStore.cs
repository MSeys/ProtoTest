namespace ProtoTest.Core.Internal;

/// <summary>
/// Run-scoped store for the resources tests owned. Like findings, it outlives the individual test
/// contexts so ownership reaches the run's reports and run gates.
/// </summary>
internal sealed class ProtoResourceReportStore : IProtoReportSource
{
    private readonly ProtoLock _gate = new();
    private readonly List<ProtoReportItem> _items = [];

    public void Add(string owner, IEnumerable<ProtoResourceSnapshot> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(resources);

        lock (_gate)
        {
            foreach (var resource in resources)
            {
                _items.Add(resource.ToReportItem("Test resources", scope: "test", displayGroup: owner));
            }
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
