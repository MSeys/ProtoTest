namespace ProtoTest.Http;

using ProtoTest.Core;

internal sealed record ProtoHttpBaseAddressRegistration(
    string ProtocolName,
    string ClientName,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> ResolveAsync);

