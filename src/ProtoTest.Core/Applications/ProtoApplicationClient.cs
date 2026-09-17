namespace ProtoTest.Core;

/// <summary>A client of a protocol registered under an application.</summary>
public sealed record ProtoApplicationClient(string ProtocolName, string ClientName);
