namespace ProtoTest.Core;

/// <summary>Exposes files produced by a sink so tracing can bundle them as run-level artifacts.</summary>
public interface IProtoSinkArtifactSource
{
    /// <summary>Returns the artifacts produced by the most recent successful export.</summary>
    IReadOnlyCollection<ProtoTestAttachment> GetArtifacts();
}
