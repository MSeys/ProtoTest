namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest.Internal;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

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
        var application = ProtoApplicationResolution.ResolveApplicationName(context);

        var requested = clientName ?? ProtoApplicationResolution.ResolveClientName(context, "Rest", fallback: "Default");
        var resolvedName = application is null ? requested : $"{application}:{requested}";

        var registration = context.Services
            .GetServices<ProtoHttpBaseAddressRegistration>()
            .LastOrDefault(item => string.Equals(item.ProtocolName, "Rest", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(item.ClientName, resolvedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ClientName, requested, StringComparison.OrdinalIgnoreCase)));
        var alias = context.Services
            .GetServices<ProtoHttpClientAliasRegistration>()
            .LastOrDefault(item => string.Equals(item.ProtocolName, "Rest", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(item.ClientName, resolvedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ClientName, requested, StringComparison.OrdinalIgnoreCase)));

        var httpClient = context.TryClient<HttpClient>(alias?.SourceClientName ?? resolvedName);
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? transportResolver = null;
        if (httpClient is null || httpClient.BaseAddress is null)
        {
            // No URL configured for this client: fall back to the application's in-process transport,
            // rooted at the client's configured endpoint path.
            var transport = ProtoApplicationResolution.ResolveTransportClient(context, application);
            if (transport is not null)
            {
                httpClient = transport;
                var endpointPath = application is null
                    ? null
                    : ProtoApplication.Endpoint(context.Configuration, application, requested);
                if (!string.IsNullOrWhiteSpace(endpointPath))
                {
                    transportResolver = (_, _) => ValueTask.FromResult(new Uri(transport.BaseAddress!, endpointPath));
                }
            }
        }

        if (httpClient is null)
        {
            throw new InvalidOperationException(
                $"No HTTP client '{resolvedName}' is registered. Register it under the application, back the " +
                $"application with AddAspNetCoreServer, or set 'ProtoTest:Applications:{application}:BaseUrl'.");
        }

        var authenticatorFactory = restState?.AuthenticatorFactory;

        context.Trace.WriteEvent(
            "rest.builder.create",
            $"REST builder · {requested}",
            "ProtoTest.Rest",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["client.name"] = requested,
                ["application.name"] = application,
                ["auth.configured"] = (authenticatorFactory is not null).ToString().ToLowerInvariant(),
                ["endpoint.resolver"] = alias is not null ? "alias" : registration is not null ? "per-test" : "client"
            });

        return new RestRequestBuilder(httpClient, context, requested)
            .UseAuthenticatorFactory(authenticatorFactory)
            .UseBaseAddressResolver(transportResolver ?? alias?.ResolveEndpointAsync ?? registration?.ResolveAsync);
    }
}
