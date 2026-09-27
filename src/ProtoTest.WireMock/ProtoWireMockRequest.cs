namespace ProtoTest.WireMock;

/// <summary>
/// One request the fake served: its method and path, whether a stub matched it, and the status served.
/// </summary>
public sealed record ProtoWireMockRequest(string Method, string Path, bool Matched, int StatusCode);
