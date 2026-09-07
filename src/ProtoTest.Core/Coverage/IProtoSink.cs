namespace ProtoTest.Core;

/// <summary>
/// Defines a contract for exporting collected coverage data to an output format (e.g., JSON, HTML, Console).
/// </summary>
public interface IProtoSink
{
    /// <summary>
    /// Asynchronously exports the provided coverage items.
    /// </summary>
    Task ExportAsync(IEnumerable<CoverageItem> items, CancellationToken cancellationToken = default);
}