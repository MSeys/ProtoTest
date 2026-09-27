namespace ProtoTest.WireMock;

/// <summary>The observation payload a stub registration records: the method and path template it serves.</summary>
public sealed record WireMockStubData(string Method, string Path);
