namespace ProtoTest.Core;

/// <summary>
/// The name resolution every protocol accessor shares: the client name the call asked for, the
/// application-qualified name a registered client uses, and the lookup that lets a host-registered client
/// stay reachable from inside an <c>[Application]</c>.
/// </summary>
public static class ProtoClientResolution
{
    /// <summary>
    /// Returns the requested client name - explicit, or the application's binding, or the first registered
    /// client for the protocol, or <paramref name="fallback"/> - together with its application-qualified
    /// form. Qualification is what keeps per-application clients from colliding.
    /// </summary>
    public static (string Requested, string ResolvedName) ResolveNames(
        ProtoExecutionContext context,
        string protocolName,
        string? requested = null,
        string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        var application = ProtoApplicationResolution.ResolveApplicationName(context);
        var name = requested
            ?? ProtoApplicationResolution.ResolveClientName(context, protocolName, fallback: fallback ?? "Default");
        return (name, Qualify(name, application));
    }

    /// <summary>Qualifies a client name with its application, so per-application clients never collide.</summary>
    public static string Qualify(string name, string? applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return applicationName is null ? name : $"{applicationName}:{name}";
    }

    /// <summary>
    /// Scopes a client name with its protocol, so two protocols can own the same name. This is the name a
    /// protocol provider registers its client under and the first name an accessor looks up.
    /// </summary>
    public static string ScopedName(string? protocol, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return string.IsNullOrWhiteSpace(protocol) ? name : $"{protocol}:{name}";
    }

    /// <summary>
    /// Finds the client for <paramref name="protocol"/> under the scoped qualified name, then the scoped
    /// requested name, then under the bare names: a host- or user-registered client stays reachable by
    /// its own name. <paramref name="exclude"/> keeps the application's transport from satisfying a name
    /// lookup. The returned <c>ResolvedName</c> is the logical (application-qualified, unscoped) name that
    /// matched, so endpoint and registration lookups stay on the user's naming; <c>RegisteredName</c> is
    /// the exact registry key that was hit, which is the identity the client's trace entity uses.
    /// </summary>
    public static (TClient? Client, string ResolvedName, string? RegisteredName) Find<TClient>(
        ProtoExecutionContext context,
        string protocol,
        string requested,
        string resolvedName,
        TClient? exclude = null)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        var requestedDiffers = !string.Equals(resolvedName, requested, StringComparison.Ordinal);

        var scopedResolved = ScopedName(protocol, resolvedName);
        var client = context.TryClient<TClient>(scopedResolved);
        if (client is not null && !ReferenceEquals(client, exclude))
        {
            return (client, resolvedName, scopedResolved);
        }

        if (requestedDiffers)
        {
            var scopedRequested = ScopedName(protocol, requested);
            client = context.TryClient<TClient>(scopedRequested);
            if (client is not null && !ReferenceEquals(client, exclude))
            {
                return (client, requested, scopedRequested);
            }
        }

        client = context.TryClient<TClient>(resolvedName);
        if (client is not null && !ReferenceEquals(client, exclude))
        {
            return (client, resolvedName, resolvedName);
        }

        if (requestedDiffers)
        {
            client = context.TryClient<TClient>(requested);
            if (client is not null && !ReferenceEquals(client, exclude))
            {
                return (client, requested, requested);
            }
        }

        return (null, resolvedName, null);
    }

    /// <summary>
    /// Returns the registry key a client instance is registered under for this test, or
    /// <see langword="null"/> when it is not registered. <see cref="Find{TClient}"/> returns the key of
    /// the client that satisfied a lookup; this form resolves an instance the caller already holds -
    /// the application's in-process transport, for example - so its trace entity uses the same identity.
    /// </summary>
    public static string? RegisteredName(ProtoExecutionContext context, object client)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(client);
        return context.TryClientName(client);
    }
}
