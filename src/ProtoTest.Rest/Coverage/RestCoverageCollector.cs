namespace ProtoTest.Rest;

using ProtoTest.Core;

/// <summary>Aggregates REST calls as route coverage from the client's observations.</summary>
public sealed class RestCoverageCollector(string targetName)
    : ProtoCoverageCollector(targetName, ProtoRestBuilder.Protocol.ResponseObservationKind)
{
    public override string Category => ProtoRestBuilder.Protocol.CoverageCategory;
}
