namespace ProtoTest.Core.Internal;

/// <summary>Holds the gate verdicts so they appear in the run's reports as their own category.</summary>
internal sealed class ProtoRunGateReportSource : ProtoReportItemStore
{
    public void Publish(IReadOnlyList<ProtoReportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Replace(items);
    }
}
