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
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? BaseAddressResolver,
    string? ClientEntityName = null);

/// <summary>
/// Resolves the HTTP client a protocol accessor uses for the current test: the protocol's scoped client
/// first, then a host- or user-registered client under the requested name, then a uniquely named client
/// of the protocol from another application, then the application's in-process transport rooted at the
/// endpoint the client registered.
/// </summary>
public static class ProtoHttpClientResolver
{
    /// <summary>
    /// Returns the HTTP client for <paramref name="protocolName"/>: an explicit
    /// <paramref name="clientName"/> (qualified with the application, then unqualified; an already
    /// qualified name is exact), then the application's bound client, then its first registered client,
    /// then "Default". A bare name that misses resolves the one client of the protocol registered under
    /// that unqualified name, whichever application owns it; several throw naming the qualified
    /// candidates. When the client has no base address and no per-test resolver owns it, the winning
    /// application's in-process transport serves it, rooted at the endpoint the client registered.
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

        // First registration wins, matching the client initializer. One entry holds the endpoint and
        // the per-test resolver, so both lookups read the same record.
        var entry = FindEntry(context, protocolName, requested, resolvedName);

        // The application's own transport registers under a context client name; it is not a host
        // client the requested name can resolve to, so it never wins the bare lookup.
        var transport = application is null
            ? null
            : ProtoApplicationResolution.ResolveTransportClient(context, application);

        var lookup = ProtoClientResolution.Find<HttpClient>(context, protocolName, requested, resolvedName, transport);
        var client = lookup.Client;
        resolvedName = lookup.ResolvedName;
        // The entity the operation links to is the client that actually serves the request: the registry
        // key that was hit, or the transport's key when the application transport serves it.
        var clientEntityName = lookup.RegisteredName;

        if (client is not null
            && ProtoClientResolution.QualifiedApplication(resolvedName) is { } resolvedApplication
            && !string.Equals(resolvedApplication, application, StringComparison.OrdinalIgnoreCase))
        {
            // A unique-name lookup resolved another application's client: that application owns the
            // entry and the in-process transport this request falls back to, not the ambient one.
            application = resolvedApplication;
            transport = ProtoApplicationResolution.ResolveTransportClient(context, application);
            entry = FindEntry(context, protocolName, requested, resolvedName);
        }

        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? transportResolver = null;
        if ((client is null || client.BaseAddress is null) && entry?.BaseAddressResolver is null)
        {
            // No URL configured and no per-test resolver owns the address: fall back to the
            // application's in-process transport, rooted at the endpoint the client was registered with.
            if (transport is not null)
            {
                client = transport;
                clientEntityName = ProtoClientResolution.RegisteredName(context, transport) ?? clientEntityName;
                var endpointPath = entry?.Endpoint is null || application is null
                    ? null
                    : ProtoApplication.Endpoint(context.Configuration, application, entry.Endpoint);
                if (!string.IsNullOrWhiteSpace(endpointPath))
                {
                    transportResolver = (_, _) => ValueTask.FromResult(new Uri(transport.BaseAddress!, endpointPath));
                }
            }
        }

        if (client is null)
        {
            var registered = ProtoClientResolution.RegisteredNames<HttpClient>(context, protocolName);
            var registeredHint = registered.Count == 0
                ? string.Empty
                : $" Registered for this protocol: {string.Join(", ", registered.Select(name => $"'{name}'"))}.";
            throw new InvalidOperationException(
                $"No HTTP client '{resolvedName}' is registered.{registeredHint} Register it under the application, " +
                $"back the application with AddAspNetCoreServer, or set 'ProtoTest:Applications:{application}:BaseUrl'.");
        }

        // The resolver named is the one the request will actually use: a per-test resolver (transport or
        // base-address registration) beats the registered client's own base address.
        var endpointResolver = transportResolver is not null
            ? "transport"
            : entry?.BaseAddressResolver is not null
                ? "per-test"
                : "client";

        return new ProtoHttpClientResolution(
            client,
            requested,
            resolvedName,
            application,
            endpointResolver,
            transportResolver ?? entry?.BaseAddressResolver,
            clientEntityName ?? resolvedName);
    }

    private static ProtoHttpClientEntry? FindEntry(
        ProtoExecutionContext context,
        string protocolName,
        string requested,
        string resolvedName)
        => context.Services
            .GetServices<ProtoHttpClientEntry>()
            .FirstOrDefault(item => Matches(item.ProtocolName, item.ClientName, protocolName, resolvedName, requested));

    private static bool Matches(
        string itemProtocol,
        string itemClient,
        string protocolName,
        string resolvedName,
        string requested)
        => string.Equals(itemProtocol, protocolName, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(itemClient, resolvedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemClient, requested, StringComparison.OrdinalIgnoreCase));

}
