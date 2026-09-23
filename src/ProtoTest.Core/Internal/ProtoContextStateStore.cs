namespace ProtoTest.Core.Internal;

using System.Collections.Concurrent;

internal sealed class ProtoContextStateStore
{
    private readonly ConcurrentDictionary<(string? Key, Type Type), IProtoContext> _contexts = new();

    public void Set<T>(string? key, T context) where T : class, IProtoContext
    {
        ArgumentNullException.ThrowIfNull(context);
        _contexts[(key, typeof(T))] = context;
    }

    public T? TryGet<T>(string? key) where T : class, IProtoContext
        => _contexts.TryGetValue((key, typeof(T)), out var context) ? (T)context : null;

    public T Get<T>(string? key) where T : class, IProtoContext
        => TryGet<T>(key)
           ?? throw new InvalidOperationException(key is null
               ? $"No context of type '{typeof(T).Name}' registered."
               : $"No context of type '{typeof(T).Name}' registered under key '{key}'.");
}
