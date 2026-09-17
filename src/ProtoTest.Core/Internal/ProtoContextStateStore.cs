namespace ProtoTest.Core.Internal;

using System.Collections.Concurrent;

internal sealed class ProtoContextStateStore
{
    private readonly ConcurrentDictionary<Type, IProtoContext> _contexts = new();

    public void Set<T>(T context) where T : class, IProtoContext
    {
        ArgumentNullException.ThrowIfNull(context);
        _contexts[typeof(T)] = context;
    }

    public T? TryGet<T>() where T : class, IProtoContext
        => _contexts.TryGetValue(typeof(T), out var context) ? (T)context : null;

    public T Get<T>() where T : class, IProtoContext
        => TryGet<T>()
           ?? throw new InvalidOperationException($"No context of type '{typeof(T).Name}' registered.");
}
