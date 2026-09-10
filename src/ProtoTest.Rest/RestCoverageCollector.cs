namespace ProtoTest.Rest;

using ProtoTest.Core;

public class RestCoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "REST";
}
