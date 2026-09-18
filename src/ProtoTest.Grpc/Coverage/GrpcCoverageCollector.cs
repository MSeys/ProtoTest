namespace ProtoTest.Grpc;

using ProtoTest.Core;

/// <summary>Aggregates gRPC calls as service/method coverage from the client's observations.</summary>
public sealed class GrpcCoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "gRPC";

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
           && string.Equals(observation.Kind, "grpc.response", StringComparison.Ordinal);
}
