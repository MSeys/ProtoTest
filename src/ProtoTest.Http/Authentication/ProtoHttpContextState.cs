namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Per-test HTTP protocol state: the authenticator the test's <c>[Auth]</c> attributes resolved to and
/// the request sequence attachment names use. One type keyed by protocol, so REST, GraphQL and any
/// later HTTP protocol each keep their own instance without a state class per protocol.
/// </summary>
public sealed class ProtoHttpContextState : IProtoContext
{
    private int _requestSequence;

    public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; init; }

    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}
