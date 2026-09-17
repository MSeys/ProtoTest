using ProtoTest.Core;
using ProtoTest.Http;

namespace ProtoTest.Rest.Internal;

/// <summary>
/// Per-test REST state. Kept protocol-local (rather than shared with GraphQL) because both protocol
/// lifecycle hooks run for every test and would otherwise overwrite each other's authenticator.
/// </summary>
internal sealed class RestContextState : IProtoContext
{
    private int _requestSequence;

    public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; set; }

    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}
