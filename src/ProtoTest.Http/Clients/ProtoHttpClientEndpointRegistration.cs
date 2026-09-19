namespace ProtoTest.Http;

/// <summary>
/// Records the application endpoint a named HTTP client was registered with, so the resolver can root
/// an in-process transport at the registered path without re-reading the client's initializer.
/// </summary>
public sealed record ProtoHttpClientEndpointRegistration(string ClientName, string? Endpoint);
