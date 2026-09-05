namespace ProtoTest.Rest.Extensions;

using ProtoTest.Core;
using ProtoTest.Rest.Internal;
using System.Net.Http;
using System.Xml;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for initiating a REST request using a registered named HttpClient.
    /// </summary>
    public static RestRequestBuilder Rest(this ProtoExecutionContext context, string clientName = "Default")
    {
        ArgumentNullException.ThrowIfNull(context);
        var restState = context.TryContext<RestContextState>();

        var targetClientName = clientName ?? restState?.ClientName ?? "Default";

        var httpClient = context.Client<HttpClient>(targetClientName);
        var authenticator = restState?.Authenticator;

        return new RestRequestBuilder(httpClient, context, authenticator);
    }
}