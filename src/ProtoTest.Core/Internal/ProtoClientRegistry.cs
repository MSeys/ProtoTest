namespace ProtoTest.Core.Internal;

/// <summary>
/// Resolves the named clients registered for a test by type and name. Ownership and release live in
/// <see cref="ProtoResourceRegistry"/>; this registry only answers lookups.
/// </summary>
internal sealed class ProtoClientRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<ClientKey, object> _clients = new(ClientKeyComparer.Instance);
    private int _sealed;

    /// <summary>Stops further registrations once the execution context starts releasing resources.</summary>
    public void Seal() => Interlocked.Exchange(ref _sealed, 1);

    public void Register<TClient>(TClient client, string name) where TClient : class
    {
        ArgumentNullException.ThrowIfNull(client);
        var key = BuildKey<TClient>(name);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_sealed != 0, this);
            if (_clients.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"A client of type '{typeof(TClient).Name}' is already registered with name '{name}'.");
            }

            _clients[key] = client;
        }
    }

    public TClient Get<TClient>(string name) where TClient : class
        => TryGet<TClient>(name)
           ?? throw new InvalidOperationException(
               $"No client of type '{typeof(TClient).Name}' registered with name '{name}' in current ProtoExecutionContext.");

    public TClient? TryGet<TClient>(string name) where TClient : class
    {
        var key = BuildKey<TClient>(name);
        lock (_gate)
        {
            return _clients.TryGetValue(key, out var client) && client is TClient typedClient
                ? typedClient
                : null;
        }
    }

    private static ClientKey BuildKey<TClient>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ClientKey(typeof(TClient), name);
    }
}
