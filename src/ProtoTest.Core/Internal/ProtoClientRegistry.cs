namespace ProtoTest.Core.Internal;

/// <summary>
/// Resolves the named clients registered for a test by type and name. Ownership and release live in
/// <see cref="ProtoResourceRegistry"/>; this registry only answers lookups.
/// </summary>
internal sealed class ProtoClientRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<ClientKey, object> _clients = new(ClientKeyComparer.Instance);
    private readonly List<ClientKey> _registrationOrder = [];
    private int _sealed;

    /// <summary>Stops further registrations once the execution context starts releasing resources.</summary>
    public void Seal() => Interlocked.Exchange(ref _sealed, 1);

    public void Register<TClient>(TClient client, string name) where TClient : class
    {
        ArgumentNullException.ThrowIfNull(client);
        var key = BuildKey(typeof(TClient), name);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_sealed != 0, this);
            if (_clients.TryGetValue(key, out var existing))
            {
                // A shared provider (the ASP.NET Core transport) can legitimately run once per protocol
                // chain that names it; re-registering the same instance is a no-op, a different one is a
                // configuration conflict.
                if (ReferenceEquals(existing, client)) return;
                throw new InvalidOperationException(
                    $"A client of type '{typeof(TClient).Name}' is already registered with name '{name}'.");
            }

            _clients[key] = client;
            _registrationOrder.Add(key);
        }
    }

    /// <summary>
    /// Registers a non-owning lookup alias for a client that is already registered under its scoped name,
    /// so its bare name keeps working when exactly one protocol owns that name. An existing registration
    /// under the bare name wins: a user- or host-registered client is never replaced.
    /// </summary>
    public void RegisterAlias(Type clientType, string name, object client)
    {
        ArgumentNullException.ThrowIfNull(clientType);
        ArgumentNullException.ThrowIfNull(client);
        var key = BuildKey(clientType, name);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_sealed != 0, this);
            if (_clients.TryAdd(key, client))
            {
                _registrationOrder.Add(key);
            }
        }
    }

    /// <summary>
    /// Replaces the client registered under <paramref name="name"/> and reports whether the entry
    /// changed. The registration order is kept, so a snapshot still yields the replacement once.
    /// Returns <see langword="false"/> when the instance is already registered under the key - a second
    /// ownership registration would dispose the same instance twice. Throws when nothing is registered
    /// under the key: a replacement always follows a registration, it never creates one.
    /// </summary>
    public bool Replace<TClient>(TClient client, string name) where TClient : class
    {
        ArgumentNullException.ThrowIfNull(client);
        var key = BuildKey(typeof(TClient), name);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_sealed != 0, this);
            if (_clients.TryGetValue(key, out var existing))
            {
                if (ReferenceEquals(existing, client))
                {
                    return false;
                }

                _clients[key] = client;
                return true;
            }
        }

        throw new InvalidOperationException(DescribeMissing(typeof(TClient), name));
    }

    /// <summary>
    /// Finds the registration name a client instance is registered under. The registry keys clients by
    /// their scoped name while operations trace entities, so the request path resolves the exact key
    /// here once instead of deriving it again.
    /// </summary>
    public string? FindName(object client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            foreach (var (key, value) in _clients)
            {
                if (ReferenceEquals(value, client))
                {
                    return key.Name;
                }
            }

            return null;
        }
    }

    public object? TryGet(Type clientType, string name)
    {
        ArgumentNullException.ThrowIfNull(clientType);
        var key = BuildKey(clientType, name);
        lock (_gate)
        {
            return _clients.TryGetValue(key, out var client) ? client : null;
        }
    }

    /// <summary>Snapshots the registered clients in registration order; used by the completion phase.</summary>
    public IReadOnlyList<object> Snapshot()
    {
        lock (_gate)
        {
            // Aliases are lookups, not additional clients. Complete each instance once.
            return [.. _registrationOrder
                .Select(key => _clients[key])
                .Distinct(ReferenceEqualityComparer.Instance)];
        }
    }

    public TClient Get<TClient>(string name) where TClient : class
        => TryGet<TClient>(name)
           ?? throw new InvalidOperationException(DescribeMissing(typeof(TClient), name));

    public TClient? TryGet<TClient>(string name) where TClient : class
    {
        var key = BuildKey(typeof(TClient), name);
        lock (_gate)
        {
            return _clients.TryGetValue(key, out var client) && client is TClient typedClient
                ? typedClient
                : null;
        }
    }

    /// <summary>
    /// A miss names the clients that are registered for the type: when two protocols own the same bare
    /// name, no alias exists and this is the only place the scoped candidates can be listed.
    /// </summary>
    private string DescribeMissing(Type clientType, string name)
    {
        string[] registered;
        lock (_gate)
        {
            registered = _clients.Keys
                .Where(key => key.ClientType == clientType)
                .Select(key => key.Name)
                .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var hint = registered.Length == 0
            ? string.Empty
            : $" Registered for this type: {string.Join(", ", registered)}.";
        return $"No client of type '{clientType.Name}' registered with name '{name}' in current ProtoExecutionContext.{hint}";
    }

    private static ClientKey BuildKey(Type clientType, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ClientKey(clientType, name);
    }
}
