namespace ProtoTest.Core;

internal sealed class ProtoClientRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<ClientKey, Registration> _clients = new(ClientKeyComparer.Instance);
    private readonly List<ClientKey> _registrationOrder = [];
    private int _disposeStarted;

    public void Register<TClient>(TClient client, string name, bool disposeWithContext) where TClient : class
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
            _clients[key] = new Registration(client, disposeWithContext);
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
            return _clients.TryGetValue(key, out var registration) && registration.Client is TClient typedClient
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

        List<(ClientKey Key, Registration Registration)> clients;
        lock (_gate)
        {
            clients = [.. _registrationOrder.AsEnumerable().Reverse().Select(key => (key, _clients[key]))];
            _clients.Clear();
            _registrationOrder.Clear();
        }

        var exceptions = new List<Exception>();
        foreach (var (key, (client, disposeWithContext)) in clients)
        {
            if (!disposeWithContext)
            {
                // Shared clients outlive the test; the context only drops its reference.
                trace.WriteEvent(
                    "client.release",
                    $"Release · {key.Name} ({key.ClientType.Name})",
                    "ProtoTest.Core",
                    ProtoTracePhase.Teardown,
                    ProtoTraceOutcome.Succeeded,
                    new Dictionary<string, string?>
                    {
                        ["client.name"] = key.Name,
                        ["client.type"] = key.ClientType.FullName,
                        ["instance.type"] = client.GetType().FullName
                    });
                continue;
            }

            using var operation = trace
                .Operation("client.dispose", $"Dispose · {key.Name} ({key.ClientType.Name})", "ProtoTest.Core")
                .During(ProtoTracePhase.Teardown)
                .With("client.name", key.Name)
                .With("client.type", key.ClientType.FullName)
                .With("instance.type", client.GetType().FullName)
                .Begin();
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

    private readonly record struct Registration(object Client, bool DisposeWithContext);

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
