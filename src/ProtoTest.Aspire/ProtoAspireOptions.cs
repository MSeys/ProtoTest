namespace ProtoTest.Aspire;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Choices for one Aspire AppHost a suite composes: which endpoint of each resource becomes its
/// application target, which application name a resource is published under, and extra settings the
/// AppHost reads. Values set here travel to the AppHost as command-line arguments on top of the run's
/// configuration and the settings earlier infrastructure published, so they win over both; the
/// AppHost otherwise reads its own sources.
/// </summary>
public sealed class ProtoAspireOptions
{
    /// <summary>
    /// The selection key that starts the AppHost: a suite sets <c>ProtoTest:Aspire:Enabled=true</c>
    /// (an environment variable in a run script) to resolve its application and infrastructure targets
    /// through the AppHost. A configured provider earlier in a chain wins regardless; nothing but the
    /// AppHost reads this key. To select only one resource - and start the AppHost only for it - set
    /// its key from <see cref="ResourceSelectionKey"/> instead.
    /// </summary>
    public const string SelectionKey = "ProtoTest:Aspire:Enabled";

    /// <summary>
    /// Gets the selection key that starts the AppHost for one resource, for example
    /// <c>ProtoTest:Aspire:Resources:postgres:Enabled</c> (the environment form is
    /// <c>ProtoTest__Aspire__Resources__postgres__Enabled=true</c>). Set it to resolve exactly the
    /// targets that resource serves through the AppHost while the run's other targets stay on their
    /// other providers, and the AppHost publishes only that resource's keys - an in-process
    /// application with AppHost infrastructure, or the reverse. The resource name is the identity the
    /// AppHost registered it under; <see cref="SelectionKey"/> still selects every resource at once.
    /// </summary>
    public static string ResourceSelectionKey(string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        return $"ProtoTest:Aspire:Resources:{resource}:Enabled";
    }

    /// <summary>
    /// Whether the run selected the AppHost for one resource: the global key or the resource's own
    /// key has a value. The AppHost piece reads the same two keys its providers' selection condition
    /// holds on, so what it publishes cannot disagree with what the chain resolved.
    /// </summary>
    internal static bool IsSelected(IConfiguration configuration, string resource)
        => HasValue(configuration, SelectionKey) || HasValue(configuration, ResourceSelectionKey(resource));

    private static bool HasValue(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key]);

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
