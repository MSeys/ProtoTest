namespace ProtoTest.Rest.Internal;

internal sealed record RestBaseAddressRegistration(
    string ClientName,
    Func<ProtoTest.Core.ProtoExecutionContext, CancellationToken, ValueTask<Uri>> ResolveAsync);
