namespace ProtoTest.Core.Internal;

/// <summary>Holds the gate findings so they appear in the run's reports.</summary>
internal sealed class ProtoRunGateReportSource : IProtoReportSource
{
    private ProtoReportItem[] _items = [];

    public void Publish(IReadOnlyList<ProtoReportItem> items) => _items = [.. items];

    public IEnumerable<ProtoReportItem> GetReportItems() => _items;
}
