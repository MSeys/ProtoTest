namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest.Internal;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for initiating a REST request. Inside an <c>[Application]</c> the default or bound
    /// REST client is used unless <paramref name="clientName"/> names another.
    /// </summary>
    public static RestRequestBuilder Rest(this ProtoExecutionContext context, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var restState = context.TryResolve<RestContextState>();
        var resolution = ProtoHttpClientResolver.Resolve(context, "Rest", clientName);

        var authenticatorFactory = restState?.AuthenticatorFactory;

        // The builder works with the registered target name: it is the identity clients, observations,
        // and collectors agree on. The requested name is only how the caller addressed the client.
        return new RestRequestBuilder(resolution.Client, context, resolution.ResolvedName)
            .UseAuthenticatorFactory(authenticatorFactory)
            .UseBaseAddressResolver(resolution.BaseAddressResolver);
    }
}
