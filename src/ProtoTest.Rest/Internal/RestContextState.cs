using ProtoTest.Core;
using ProtoTest.Http;

namespace ProtoTest.Rest.Internal;

internal sealed class RestContextState : IProtoContext
{
    private int _requestSequence;

    public string ClientName { get; set; } = "Default";
    public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; set; }

    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}
