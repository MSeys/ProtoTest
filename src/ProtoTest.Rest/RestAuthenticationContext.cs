namespace ProtoTest.Rest;

using ProtoTest.Core;

/// <summary>Context supplied to a REST authenticator when a request is sent.</summary>
public sealed record RestAuthenticationContext(
    HttpRequestMessage Request,
    ProtoExecutionContext Test,
    string ClientName);
