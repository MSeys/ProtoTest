namespace ProtoTest.Core;

internal sealed class ProtoClientRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<ClientKey, object> _clients = new(ClientKeyComparer.Instance);
    private readonly List<ClientKey> _registrationOrder = [];
    private int _disposeStarted;

    public void Register<TClient>(TClient client, string name) where TClient : class
    {
        ArgumentNullException.ThrowIfNull(client);
        var key = BuildKey<TClient>(name);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
            if (_clients.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"A client of type '{typeof(TClient).Name}' is already registered with name '{name}'.");
            }

            _registrationOrder.Add(key);
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

    public async ValueTask<IReadOnlyList<Exception>> DisposeClientsAsync(IProtoTraceWriter trace)
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return [];
        }

        List<(ClientKey Key, object Client)> clients;
        lock (_gate)
        {
            clients = [.. _registrationOrder.AsEnumerable().Reverse().Select(key => (key, _clients[key]))];
            _clients.Clear();
            _registrationOrder.Clear();
        }

        var exceptions = new List<Exception>();
        foreach (var (key, client) in clients)
        {
            using var operation = trace.StartOperation(
                "client.dispose",
                $"Dispose · {key.Name} ({key.ClientType.Name})",
                "ProtoTest.Core",
                ProtoTracePhase.Teardown,
                new Dictionary<string, string?>
                {
                    ["client.name"] = key.Name,
                    ["client.type"] = key.ClientType.FullName,
                    ["instance.type"] = client.GetType().FullName
                });
            try
            {
                if (client is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (client is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                operation.Succeed();
            }
            catch (Exception exception)
            {
                operation.Fail(exception);
                exceptions.Add(exception);
            }
        }

        return exceptions;
    }

    private static ClientKey BuildKey<TClient>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ClientKey(typeof(TClient), name);
    }

    private readonly record struct ClientKey(Type ClientType, string Name);

    private sealed class ClientKeyComparer : IEqualityComparer<ClientKey>
    {
        public static ClientKeyComparer Instance { get; } = new();

        public bool Equals(ClientKey x, ClientKey y)
            => x.ClientType == y.ClientType
               && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode(ClientKey obj)
            => HashCode.Combine(obj.ClientType, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
