namespace ProtoTest.Grpc.Authentication;

using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Per-test gRPC state. Kept protocol-local like REST and GraphQL because every protocol lifecycle
/// hook runs for every test and would otherwise overwrite each other's authenticator.
/// </summary>
internal sealed class GrpcContextState : IProtoContext
{
    public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; init; }
}

/// <summary>Resolves <c>[Auth]</c> authenticators for the test; the client comes from <c>[Application]</c>.</summary>
internal sealed class GrpcLifecycleHook() : ProtoHttpAuthLifecycleHook("Grpc")
{
    protected override void SetContext(
        ProtoExecutionContext context,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
        => context.SetContext(new GrpcContextState
        {
            AuthenticatorFactory = authenticatorFactory
        });
}
