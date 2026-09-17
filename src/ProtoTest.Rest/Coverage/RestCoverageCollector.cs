namespace ProtoTest.Rest;

using ProtoTest.Core;

public sealed class RestCoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "REST";

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
           && string.Equals(observation.Kind, "http.response", StringComparison.Ordinal);
}
