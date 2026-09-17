namespace ProtoTest.Http;

using ProtoTest.Core;

public sealed record ProtoHttpBaseAddressRegistration(
    string ProtocolName,
    string ClientName,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> ResolveAsync);
