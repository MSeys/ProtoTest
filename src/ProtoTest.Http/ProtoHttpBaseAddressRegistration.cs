namespace ProtoTest.Http;

using ProtoTest.Core;

public sealed record ProtoHttpBaseAddressRegistration(
    string ProtocolName,
    string ClientName,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> ResolveAsync);

/// <summary>Maps a protocol target to an HTTP client already owned by the current test.</summary>
public sealed record ProtoHttpClientAliasRegistration(
    string ProtocolName,
    string ClientName,
    string SourceClientName,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> ResolveEndpointAsync);
