namespace ProtoTest.Core;

using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Resolves which client a protocol accessor should use for the current application.</summary>
public static class ProtoApplicationResolution
{
    /// <summary>
    /// Returns the client for <paramref name="protocolName"/>: an explicit <paramref name="requested"/>
    /// name wins, then an <c>[Application]</c> binding, then the application's first registered client,
    /// then <paramref name="fallback"/>.
    /// </summary>
    public static string ResolveClientName(
        ProtoExecutionContext context,
        string protocolName,
        string? requested = null,
        string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);

        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        var state = ResolveState(context);
        var bound = state?.Client(protocolName);
        if (!string.IsNullOrWhiteSpace(bound))
        {
            return bound;
        }

        var registry = context.TryService<ProtoApplicationRegistry>();
        if (registry is not null && state is not null)
        {
            var defaultClient = registry.DefaultClient(state.ApplicationName, protocolName);
            if (!string.IsNullOrWhiteSpace(defaultClient))
            {
                return defaultClient;
            }
        }

        return fallback ?? throw new InvalidOperationException(
            state is null
                ? $"No application is selected for this test. Apply [Application(name)] or pass a {protocolName} client name to the accessor."
                : $"Application '{state.ApplicationName}' has no {protocolName} client registered. Register one in AddApplication or pass a client name to the accessor.");
    }

    /// <summary>Returns the name of the application selected for the test, or <see langword="null"/>.</summary>
    public static string? ResolveApplicationName(ProtoExecutionContext context)
        => ResolveState(context)?.ApplicationName;

    /// <summary>
    /// Returns the in-process transport client backing <paramref name="applicationName"/>, or null.
    /// Used to serve an application's HTTP clients when no base address is configured.
    /// </summary>
    public static HttpClient? ResolveTransportClient(ProtoExecutionContext context, string? applicationName)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return null;
        }

        foreach (var transport in context.Services.GetServices<ProtoApplicationTransport>())
        {
            if (string.Equals(transport.ApplicationName, applicationName, StringComparison.OrdinalIgnoreCase))
            {
                return context.TryClient<HttpClient>(transport.ClientName);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the application state the <c>[Application]</c> attribute set for the test, or
    /// <see langword="null"/> when the test selected no application. The attribute's
    /// <see cref="ApplicationAttribute.BeforeTestAsync"/> is the one writer: a context without state
    /// did not run an <c>[Application]</c> attribute, so the missing selection is reported rather than
    /// replaced by a second reading of the attribute.
    /// </summary>
    public static ProtoApplicationState? ResolveState(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryResolve<ProtoApplicationState>();
    }
}
