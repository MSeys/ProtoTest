namespace ProtoTest.Rest;

using ProtoTest.Core;

public class RestCoverageCollector(string targetName) : ProtoCollector(targetName)
{
    public override string Category => "REST";
}