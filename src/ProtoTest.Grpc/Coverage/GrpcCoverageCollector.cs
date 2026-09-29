namespace ProtoTest.Grpc;

using ProtoTest.Core;

/// <summary>Aggregates gRPC calls as service/method coverage from the client's observations.</summary>
public sealed class GrpcCoverageCollector(string targetName)
    : ProtoCoverageCollector(
        targetName,
        ProtoGrpcBuilder.Protocol.ResponseObservationKind,
        // gRPC service and method names are case-sensitive.
        StringComparer.Ordinal)
{
    public override string Category => ProtoGrpcBuilder.Protocol.CoverageCategoryOrName;
}
