namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// The HTTP client a protocol accessor will use, together with the per-test resolver that supplies
/// its base address and the names requests and traces should use.
/// </summary>
public sealed record ProtoHttpClientResolution(
    HttpClient Client,
    string RequestedName,
    string ResolvedName,
    string? ApplicationName,
    string? SourceClientName,
    string EndpointResolver,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? BaseAddressResolver)
{
    /// <summary>The name requests are traced under: the aliased source client when one is used.</summary>
    public string SourceName => SourceClientName ?? ResolvedName;
}

/// <summary>
/// Resolves the HTTP client a protocol accessor uses for the current test, shared by the HTTP-based
/// protocols so their registration and alias lookup, in-process transport fallback, and error text
/// stay identical.
/// </summary>
public static class ProtoHttpClientResolver
{
    /// <summary>
    /// Returns the HTTP client for <paramref name="protocolName"/>: an explicit
    /// <paramref name="clientName"/> (qualified with the application, then unqualified), then the
    /// application's bound client, then its first registered client, then "Default". When the client
    /// has no base address and no per-test resolver or alias owns it, the application's in-process
    /// transport serves it, rooted at the endpoint the client registered, then
    /// <paramref name="defaultEndpointName"/>, then the requested client.
    /// </summary>
    public static ProtoHttpClientResolution Resolve(
        ProtoExecutionContext context,
        string protocolName,
        string? clientName = null,
        string? defaultEndpointName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);

        var application = ProtoApplicationResolution.ResolveApplicationName(context);
        var requested = clientName ?? ProtoApplicationResolution.ResolveClientName(
            context, protocolName, fallback: "Default");
        var resolvedName = ProtoHttpClientRegistration.Qualify(requested, application);

        // First registration wins, matching the client initializer and endpoint selection.
        var registration = context.Services
            .GetServices<ProtoHttpBaseAddressRegistration>()
            .FirstOrDefault(item => Matches(item.ProtocolName, item.ClientName, protocolName, resolvedName, requested));
        var alias = context.Services
            .GetServices<ProtoHttpClientAliasRegistration>()
            .FirstOrDefault(item => Matches(item.ProtocolName, item.ClientName, protocolName, resolvedName, requested));

        // The application's own transport registers under a context client name; it is not a host
        // client the requested name can resolve to, so it never wins the unqualified fallback.
        var transport = application is null
            ? null
            : ProtoApplicationResolution.ResolveTransportClient(context, application);

        var client = context.TryClient<HttpClient>(alias?.SourceClientName ?? resolvedName);
        if (client is null && !string.Equals(resolvedName, requested, StringComparison.Ordinal))
        {
            // An explicit client name wins over its application-qualified form: a host-registered
            // client addressed by its own name is used before the application's transport.
            var requestedClient = context.TryClient<HttpClient>(requested);
            if (requestedClient is not null && !ReferenceEquals(requestedClient, transport))
            {
                client = requestedClient;
                resolvedName = requested;
            }
        }

        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? transportResolver = null;
        if ((client is null || client.BaseAddress is null) && registration is null && alias is null)
        {
            // No URL configured and no per-test resolver or alias owns the address: fall back to the
            // application's in-process transport, rooted at the endpoint the protocol names.
            if (transport is not null)
            {
                client = transport;
                // The endpoint roots the transport at the path this client was registered with; the
                // protocol's own default only applies when the client registered no endpoint.
                var endpointName = RegisteredEndpoint(context, resolvedName)
                    ?? defaultEndpointName
                    ?? requested;
                var endpointPath = application is null
                    ? null
                    : ProtoApplication.Endpoint(context.Configuration, application, endpointName);
                if (!string.IsNullOrWhiteSpace(endpointPath))
                {
                    transportResolver = (_, _) => ValueTask.FromResult(new Uri(transport.BaseAddress!, endpointPath));
                }
            }
        }

        if (client is null)
        {
            throw new InvalidOperationException(
                $"No HTTP client '{resolvedName}' is registered. Register it under the application, back the " +
                $"application with AddAspNetCoreServer, or set 'ProtoTest:Applications:{application}:BaseUrl'.");
        }

        // The resolver named is the one the request will actually use: a per-test resolver (transport,
        // alias, or base-address registration) beats the registered client's own base address.
        var endpointResolver = transportResolver is not null
            ? "transport"
            : alias?.ResolveEndpointAsync is not null
                ? "alias"
                : registration?.ResolveAsync is not null
                    ? "per-test"
                    : "client";

        return new ProtoHttpClientResolution(
            client,
            requested,
            resolvedName,
            application,
            alias?.SourceClientName,
            endpointResolver,
            transportResolver ?? alias?.ResolveEndpointAsync ?? registration?.ResolveAsync);
    }

    private static bool Matches(
        string itemProtocol,
        string itemClient,
        string protocolName,
        string resolvedName,
        string requested)
        => string.Equals(itemProtocol, protocolName, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(itemClient, resolvedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemClient, requested, StringComparison.OrdinalIgnoreCase));

    private static string? RegisteredEndpoint(ProtoExecutionContext context, string resolvedName)
        => context.Services.GetServices<ProtoHttpClientEndpointRegistration>()
            // First registration wins, matching the client initializer selection.
            .FirstOrDefault(registration =>
                string.Equals(registration.ClientName, resolvedName, StringComparison.OrdinalIgnoreCase))
            ?.Endpoint;
}
