namespace ProtoTest.Core;

/// <summary>Defines a contract for exporting normalized report data.</summary>
public interface IProtoSink
{
    /// <summary>Asynchronously exports the provided report items.</summary>
    Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default);
}
