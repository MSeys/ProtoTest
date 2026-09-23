namespace ProtoTest.Grpc;

using ProtoTest.Core;

/// <summary>Aggregates gRPC calls as service/method coverage from the client's observations.</summary>
public sealed class GrpcCoverageCollector(string targetName)
    : ProtoCoverageCollector(targetName, ProtoGrpcBuilder.Protocol.ResponseObservationKind)
{
    public override string Category => ProtoGrpcBuilder.Protocol.CoverageCategory;
}
