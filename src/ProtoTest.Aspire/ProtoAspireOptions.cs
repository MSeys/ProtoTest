namespace ProtoTest.Aspire;

/// <summary>
/// Choices for one Aspire AppHost a suite composes: which endpoint of each resource becomes its
/// application target, which application name a resource is published under, and extra settings the
/// AppHost reads. Values set here travel to the AppHost as command-line arguments; the AppHost
/// otherwise reads its own sources.
/// </summary>
public sealed class ProtoAspireOptions
{
    /// <summary>
    /// The selection key that starts the chain's AppHost providers: a suite sets
    /// <c>ProtoTest:Aspire:Enabled=true</c> (an environment variable in a run script) to resolve its
    /// application and infrastructure targets through the AppHost. A configured provider earlier in a
    /// chain wins regardless; nothing but the AppHost reads this key.
    /// </summary>
    public const string SelectionKey = "ProtoTest:Aspire:Enabled";

    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _applications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _endpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _connections = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string?> Values => _values;

    internal IReadOnlyDictionary<string, string> Applications => _applications;

    internal IReadOnlyDictionary<string, string> Endpoints => _endpoints;

    internal IReadOnlyDictionary<string, string> ConnectionStrings => _connections;

    internal bool HasConnectionString(string resource) => _connections.ContainsKey(resource);

    /// <summary>
    /// Sets one configuration key the AppHost reads, for example <c>Seed:Catalog</c>. A
    /// <see langword="null"/> value stays an empty setting, like the worker host's options.
    /// </summary>
    public ProtoAspireOptions Set(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = value ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Publishes the resource under a different application name: its address fills
    /// <c>ProtoTest:Applications:{application}:BaseUrl</c> instead of the resource's own section.
    /// </summary>
    public ProtoAspireOptions MapResource(string resource, string application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        _applications[resource] = application;
        return this;
    }

    /// <summary>
    /// Reads the named endpoint of the resource instead of its <c>http</c> endpoint, for resources
    /// that expose several (an <c>https</c> endpoint, a gRPC endpoint).
    /// </summary>
    public ProtoAspireOptions UseEndpoint(string resource, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        _endpoints[resource] = endpoint;
        return this;
    }

    /// <summary>
    /// Publishes the resource's connection string under <paramref name="key"/> instead of its endpoint
    /// under an application's <c>BaseUrl</c>: the resource is a database or broker, not an HTTP
    /// application. The target's declared registration fills the key when the AppHost starts and the
    /// environment has no value for it.
    /// </summary>
    /// <param name="resource">The AppHost resource, for example <c>postgres</c>.</param>
    /// <param name="key">The target's declared key, for example <c>ConnectionStrings:Northstar</c>.</param>
    public ProtoAspireOptions MapConnectionString(string resource, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _connections[resource] = key;
        return this;
    }

    internal string ApplicationFor(string resource)
        => _applications.TryGetValue(resource, out var application) ? application : resource;

    internal string EndpointFor(string resource)
        => _endpoints.TryGetValue(resource, out var endpoint) ? endpoint : "http";

    internal void Validate(IEnumerable<string> resources)
    {
        var declared = new HashSet<string>(resources, StringComparer.Ordinal);
        foreach (var resource in _applications.Keys.Concat(_endpoints.Keys).Concat(_connections.Keys))
        {
            if (!declared.Contains(resource))
            {
                throw new ArgumentException(
                    $"'{resource}' is mapped but no such Aspire resource is registered; register it with AddAspireAppHost or drop the mapping.",
                    nameof(resources));
            }
        }

        var applications = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in declared)
        {
            if (!applications.Add(ApplicationFor(resource)))
            {
                throw new ArgumentException(
                    $"Several Aspire resources map to the application '{ApplicationFor(resource)}'; one application holds one address.",
                    nameof(resources));
            }
        }
    }

    internal bool SameAs(ProtoAspireOptions other)
        => _values.Count == other._values.Count
            && _values.All(pair => other._values.TryGetValue(pair.Key, out var value) && value == pair.Value)
            && _applications.Count == other._applications.Count
            && _applications.All(pair => other._applications.TryGetValue(pair.Key, out var value) && value == pair.Value)
            && _endpoints.Count == other._endpoints.Count
            && _endpoints.All(pair => other._endpoints.TryGetValue(pair.Key, out var value) && value == pair.Value)
            && _connections.Count == other._connections.Count
            && _connections.All(pair => other._connections.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
