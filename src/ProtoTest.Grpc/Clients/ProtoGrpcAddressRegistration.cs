namespace ProtoTest.Grpc.Clients;

using ProtoTest.Core;

/// <summary>
/// The address of a named gRPC client resolved per test, like the HTTP base-address registrations the
/// other protocols use. Register clients with a resolver through <c>AddClient(name, resolver)</c>.
/// </summary>
public sealed record ProtoGrpcAddressRegistration(
    string ProtocolName,
    string ClientName,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> ResolveAsync);
