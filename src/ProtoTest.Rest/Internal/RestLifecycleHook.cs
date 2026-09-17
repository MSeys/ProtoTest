namespace ProtoTest.Rest.Internal;

using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>Resolves <c>[Auth]</c> authenticators for the test; the client comes from <c>[Application]</c>.</summary>
internal sealed class RestLifecycleHook() : ProtoHttpAuthLifecycleHook("Rest")
{
    protected override void SetContext(
        ProtoExecutionContext context,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
        => context.SetContext(new RestContextState
        {
            AuthenticatorFactory = authenticatorFactory
        });
}
