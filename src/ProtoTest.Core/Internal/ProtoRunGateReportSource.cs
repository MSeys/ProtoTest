namespace ProtoTest.Core.Internal;

/// <summary>Holds the gate verdicts so they appear in the run's reports as their own category.</summary>
internal sealed class ProtoRunGateReportSource : IProtoReportSource
{
    private ProtoReportItem[] _items = [];

    public void Publish(IReadOnlyList<ProtoReportItem> items) => _items = [.. items];

    public IEnumerable<ProtoReportItem> GetReportItems() => _items;
}
