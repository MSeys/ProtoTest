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
    /// form. Qualification is what keeps per-application clients from colliding; a name the caller already
    /// qualified (<c>App:Client</c>) is kept exactly as given, because it names that application and not
    /// the ambient one.
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
        return (name, IsQualified(name) ? name : Qualify(name, application));
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
    /// Returns the application part of an application-qualified client name (<c>App:Client</c>), or
    /// <see langword="null"/> when the name carries no qualification.
    /// </summary>
    public static string? QualifiedApplication(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var separator = name.IndexOf(':');
        return separator > 0 ? name[..separator] : null;
    }

    /// <summary>
    /// Returns the application-qualified names clients of <typeparamref name="TClient"/> are registered
    /// under for <paramref name="protocol"/>, sorted by name. Registry keys are protocol-scoped
    /// (<c>Rest:App:Client</c>); the protocol is stripped, so an error lists the names a caller can pass
    /// and a bare name can be matched against its unqualified part.
    /// </summary>
    public static IReadOnlyList<string> RegisteredNames<TClient>(
        ProtoExecutionContext context,
        string protocol)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        return [.. context.RegisteredClientNames(typeof(TClient))
            .Select(name => QualifiedName(protocol, name))
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Finds the client for <paramref name="protocol"/> under the scoped qualified name, then the scoped
    /// requested name, then under the bare names: a host- or user-registered client stays reachable by
    /// its own name. When all of those miss and the requested name is bare, exactly one client of the
    /// protocol whose unqualified name equals it resolves from wherever it is registered - the ambient
    /// application still wins, and two same-named clients throw naming both qualified candidates.
    /// <paramref name="exclude"/> keeps the application's transport from satisfying a name lookup. The
    /// returned <c>ResolvedName</c> is the logical (application-qualified, unscoped) name that matched, so
    /// endpoint and registration lookups stay on the user's naming; <c>RegisteredName</c> is the exact
    /// registry key that was hit, which is the identity the client's trace entity uses.
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

        // Nothing is registered under the names above. A bare name may name exactly one client on the
        // host - under another application - and resolves to it; several clients with that name must be
        // disambiguated by the caller. A name that is already qualified is exact and is never completed
        // from another application. Aliases and scoped registrations of one instance count once, and the
        // scoped registration (registered first) names the match.
        if (!requested.Contains(':'))
        {
            var candidates = new List<(TClient Client, string Qualified, string Registered)>();
            foreach (var registered in context.RegisteredClientNames(typeof(TClient)))
            {
                var qualified = QualifiedName(protocol, registered);
                if (qualified is null
                    || !string.Equals(UnqualifiedName(qualified), requested, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = context.TryClient<TClient>(registered);
                if (candidate is null
                    || ReferenceEquals(candidate, exclude)
                    || candidates.Any(item => ReferenceEquals(item.Client, candidate)))
                {
                    continue;
                }

                candidates.Add((candidate, qualified, registered));
            }

            if (candidates.Count == 1)
            {
                return (candidates[0].Client, candidates[0].Qualified, candidates[0].Registered);
            }

            if (candidates.Count > 1)
            {
                var names = candidates
                    .Select(item => item.Qualified)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Two or more clients are named '{requested}': " +
                    $"{string.Join(", ", names.Select(name => $"'{name}'"))}. " +
                    $"Qualify the name with its application (for example '{names[0]}').");
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

    /// <summary>Returns whether a client name carries an application qualification (<c>App:Client</c>).</summary>
    private static bool IsQualified(string name) => name.Contains(':');

    /// <summary>
    /// Maps a protocol-scoped registry key (<c>Rest:App:Client</c>) back to the name a caller would use
    /// (<c>App:Client</c>), or <see langword="null"/> when the key belongs to another protocol.
    /// </summary>
    private static string? QualifiedName(string protocol, string registeredName)
    {
        var prefix = $"{protocol}:";
        if (!registeredName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = registeredName[prefix.Length..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>Returns the client part of an application-qualified name, or the name itself.</summary>
    private static string UnqualifiedName(string qualifiedName)
    {
        var separator = qualifiedName.IndexOf(':');
        return separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];
    }
}
