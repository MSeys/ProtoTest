namespace ProtoTest.Rest.Internal;

using ProtoTest.Core;
using ProtoTest.Http;

internal sealed class RestLifecycleHook() : ProtoHttpAuthLifecycleHook<RestClientAttribute, IRestAuthMetadata>("Rest")
{
    protected override string GetClientName(RestClientAttribute? attribute) => attribute?.ClientName ?? "Default";

    protected override void SetContext(
        ProtoExecutionContext context,
        string clientName,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
        => context.SetContext(new RestContextState
        {
            ClientName = clientName,
            AuthenticatorFactory = authenticatorFactory
        });
}
