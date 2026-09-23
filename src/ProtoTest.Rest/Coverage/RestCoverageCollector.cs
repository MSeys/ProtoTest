namespace ProtoTest.Rest;

using ProtoTest.Core;

/// <summary>Aggregates REST calls as route coverage from the client's observations.</summary>
public sealed class RestCoverageCollector(string targetName) : ProtoCoverageCollector(targetName, "http.response")
{
    public override string Category => "REST";
}
