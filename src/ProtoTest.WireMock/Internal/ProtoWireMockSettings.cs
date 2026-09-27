namespace ProtoTest.WireMock.Internal;

/// <summary>One fake's resolved settings: its lifetime and its listen port, if fixed.</summary>
internal sealed record ProtoWireMockSettings(bool PerRun, int? Port);
