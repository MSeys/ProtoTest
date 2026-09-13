namespace ProtoTest.Core;

/// <summary>Defines a contract for exporting normalized report data.</summary>
public interface IProtoSink
{
    /// <summary>Asynchronously exports the provided report items.</summary>
    Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default);
}

/// <summary>Exposes files produced by a sink so tracing can bundle them as run-level artifacts.</summary>
public interface IProtoSinkArtifactSource
{
    /// <summary>Returns the artifacts produced by the most recent successful export.</summary>
    IReadOnlyCollection<ProtoTestAttachment> GetArtifacts();
}
