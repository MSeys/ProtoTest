using ProtoTest.Core;

namespace ProtoTest.Rest.Internal;

internal sealed class RestContextState : IProtoContext
{
    public string ClientName { get; set; } = "Default";
    public IRestAuthenticator? Authenticator { get; set; }
}