namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// One registered HTTP client target: the protocol it serves, the application and endpoint it was
/// configured for, and the per-test base-address resolver when one was supplied. One entry per
/// <c>AddClient</c>, so resolution reads one place instead of one marker record per fact.
/// </summary>
internal sealed record ProtoHttpClientEntry(
    string ProtocolName,
    string ClientName,
    string? ApplicationName,
    string? Endpoint,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? BaseAddressResolver);
