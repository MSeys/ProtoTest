namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest.Internal;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for initiating a REST request using a registered named HttpClient.
    /// </summary>
    public static RestRequestBuilder Rest(this ProtoExecutionContext context, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var restState = context.TryContext<RestContextState>();

        var targetClientName = clientName ?? restState?.ClientName ?? "Default";

        var httpClient = context.Client<HttpClient>(targetClientName);
        var authenticatorFactory = restState?.AuthenticatorFactory;
        var baseAddressRegistration = context.Services
            .GetServices<ProtoHttpBaseAddressRegistration>()
            .LastOrDefault(registration => string.Equals(
                registration.ProtocolName,
                "Rest",
                StringComparison.OrdinalIgnoreCase) && string.Equals(
                registration.ClientName,
                targetClientName,
                StringComparison.OrdinalIgnoreCase));

        return new RestRequestBuilder(httpClient, context, targetClientName, defaultAuthenticator: null)
            .UseAuthenticatorFactory(authenticatorFactory)
            .UseBaseAddressResolver(baseAddressRegistration?.ResolveAsync);
    }
}
