using ProtoTest.Core;

namespace ProtoTest.Rest.Internal;

internal sealed class RestContextState : IProtoContext
{
    private int _requestSequence;

    public string ClientName { get; set; } = "Default";
    public Func<ProtoExecutionContext, IRestAuthenticator>? AuthenticatorFactory { get; set; }

    public int NextRequestNumber() => Interlocked.Increment(ref _requestSequence);
}
