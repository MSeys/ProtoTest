namespace ProtoTest.Core;

internal sealed record ProtoTraceArtifactSource(ProtoTraceArtifact Artifact, ReadOnlyMemory<byte> Content);

