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
    string EndpointResolver,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? BaseAddressResolver);

/// <summary>
/// Resolves the HTTP client a protocol accessor uses for the current test: the protocol's scoped client
/// first, then a host- or user-registered client under the requested name, then the application's
/// in-process transport rooted at the endpoint the client registered.
/// </summary>
public static class ProtoHttpClientResolver
{
    /// <summary>
    /// Returns the HTTP client for <paramref name="protocolName"/>: an explicit
    /// <paramref name="clientName"/> (qualified with the application, then unqualified), then the
    /// application's bound client, then its first registered client, then "Default". When the client
    /// has no base address and no per-test resolver owns it, the application's in-process transport
    /// serves it, rooted at the endpoint the client registered.
    /// </summary>
    public static ProtoHttpClientResolution Resolve(
        ProtoExecutionContext context,
        string protocolName,
        string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);

        var application = ProtoApplicationResolution.ResolveApplicationName(context);
        var (requested, resolvedName) = ProtoClientResolution.ResolveNames(context, protocolName, clientName);

        // First registration wins, matching the client initializer and endpoint selection.
        var registration = context.Services
            .GetServices<ProtoHttpBaseAddressRegistration>()
            .FirstOrDefault(item => Matches(item.ProtocolName, item.ClientName, protocolName, resolvedName, requested));

        // The application's own transport registers under a context client name; it is not a host
        // client the requested name can resolve to, so it never wins the bare lookup.
        var transport = application is null
            ? null
            : ProtoApplicationResolution.ResolveTransportClient(context, application);

        var lookup = ProtoClientResolution.Find<HttpClient>(context, protocolName, requested, resolvedName, transport);
        var client = lookup.Client;
        resolvedName = lookup.ResolvedName;

        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? transportResolver = null;
        if ((client is null || client.BaseAddress is null) && registration is null)
        {
            // No URL configured and no per-test resolver owns the address: fall back to the
            // application's in-process transport, rooted at the endpoint the client was registered with.
            if (transport is not null)
            {
                client = transport;
                var endpointName = RegisteredEndpoint(context, resolvedName);
                var endpointPath = endpointName is null || application is null
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

        // The resolver named is the one the request will actually use: a per-test resolver (transport or
        // base-address registration) beats the registered client's own base address.
        var endpointResolver = transportResolver is not null
            ? "transport"
            : registration?.ResolveAsync is not null
                ? "per-test"
                : "client";

        return new ProtoHttpClientResolution(
            client,
            requested,
            resolvedName,
            application,
            endpointResolver,
            transportResolver ?? registration?.ResolveAsync);
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
