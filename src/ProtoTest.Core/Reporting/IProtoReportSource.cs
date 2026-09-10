namespace ProtoTest.Core;

/// <summary>
/// Produces a snapshot of normalized items for end-of-run reporting.
/// </summary>
public interface IProtoReportSource
{
    IEnumerable<ProtoReportItem> GetReportItems();
}
