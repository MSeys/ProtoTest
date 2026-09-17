namespace ProtoTest.Http;

/// <summary>
/// Records which application a registered HTTP target (client) belongs to, so collectors and other
/// services can resolve the target's application without a separate client configuration section.
/// </summary>
public sealed record ProtoApplicationTarget(string TargetName, string Application);
