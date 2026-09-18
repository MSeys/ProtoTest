namespace ProtoTest.Core.Internal;

using System.Collections.Concurrent;

/// <summary>
/// Thread-safe state of everything the run touched: entities (clients, contexts, servers, capabilities,
/// resources) and values (domain objects). One store, because both are the same thing - an identified item
/// with a name, a scope, merged state and a history of versions. The entity/value
/// distinction lives in the kind and in which API touched the item, not in the storage.
/// </summary>
internal sealed class ProtoItemStore
{
    private readonly ConcurrentDictionary<string, Entry> _items = new(StringComparer.Ordinal);

    /// <summary>Merges state into an item; every call is a version with the full state at that moment.</summary>
    public void SetState(
        string kind,
        string id,
        string name,
        IReadOnlyDictionary<string, string?>? state,
        string? scope,
        string? change = null,
        string? operationId = null)
    {
        Validate(kind, id, name);
        var entry = _items.GetOrAdd(Key(kind, id), _ => new Entry(kind, id));
        entry.SetState(name, state, scope, change, operationId);
    }

    /// <summary>Appends a value version: a domain object was created, changed, read or deleted.</summary>
    public void AddValue(
        string kind,
        string id,
        string name,
        string change,
        string? operationId,
        IReadOnlyDictionary<string, string?>? state,
        ProtoTraceValueSource source,
        bool inferred,
        string? scope)
    {
        Validate(kind, id, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(change);
        var entry = _items.GetOrAdd(Key(kind, id), _ => new Entry(kind, id));
        entry.AddValue(new ProtoTraceVersion(
            operationId,
            change,
            DateTimeOffset.UtcNow,
            state is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(state, StringComparer.Ordinal),
            source,
            inferred), name, scope);
    }

    public IReadOnlyList<ProtoTraceEntity> SnapshotEntities()
        => [.. _items.Values
            .Where(entry => entry.TouchedAsEntity)
            .Select(entry => entry.SnapshotEntity())
            .OrderBy(entity => entity.Kind, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)];

    public IReadOnlyList<ProtoTraceValue> SnapshotValues()
        => [.. _items.Values
            .Where(entry => entry.TouchedAsValue)
            .Select(entry => entry.SnapshotValue())
            .OrderBy(value => value.FirstSeenUtc)
            .ThenBy(value => value.Id, StringComparer.Ordinal)];

    private static string Key(string kind, string id) => $"{kind}\u001f{id}";

    private static void Validate(string kind, string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

    private sealed class Entry(string kind, string id)
    {
        private readonly ProtoLock _gate = new();
        private readonly Dictionary<string, string?> _state = new(StringComparer.Ordinal);
        private readonly List<ProtoTraceVersion> _versions = [];
        private readonly DateTimeOffset _firstSeenUtc = DateTimeOffset.UtcNow;
        private DateTimeOffset _lastSeenUtc = DateTimeOffset.UtcNow;
        private string? _name;
        private string? _scope;
        private bool _touchedAsEntity;
        private bool _touchedAsValue;

        public bool TouchedAsEntity
        {
            get { lock (_gate) { return _touchedAsEntity; } }
        }

        public bool TouchedAsValue
        {
            get { lock (_gate) { return _touchedAsValue; } }
        }

        public void SetState(
            string name,
            IReadOnlyDictionary<string, string?>? state,
            string? scope,
            string? change,
            string? operationId)
        {
            lock (_gate)
            {
                _touchedAsEntity = true;
                // The first caller knows the entity best; later state updates must not rename it.
                _name ??= name;
                var now = DateTimeOffset.UtcNow;
                _lastSeenUtc = now;
                if (scope is not null)
                {
                    _scope = scope;
                }

                if (state is not null)
                {
                    foreach (var (key, value) in state)
                    {
                        _state[key] = value;
                    }
                }

                // Every state change is a version, with the full state at that moment: a context that was
                // set and later replaced keeps its history.
                var label = change ?? (_versions.Count == 0 ? "created" : "changed");
                _versions.Add(new ProtoTraceVersion(
                    operationId,
                    label,
                    now,
                    new Dictionary<string, string?>(_state, StringComparer.Ordinal)));
            }
        }

        public void AddValue(ProtoTraceVersion version, string name, string? scope)
        {
            lock (_gate)
            {
                _touchedAsValue = true;
                _versions.Add(version);
                _lastSeenUtc = version.AtUtc;
                // The first caller knows the value best; later versions must not rename it.
                _name ??= name;
                if (scope is not null)
                {
                    _scope = scope;
                }
            }
        }

        public ProtoTraceEntity SnapshotEntity()
        {
            lock (_gate)
            {
                return new ProtoTraceEntity(
                    id,
                    kind,
                    _name ?? id,
                    _scope,
                    _firstSeenUtc,
                    _lastSeenUtc,
                    new Dictionary<string, string?>(_state, StringComparer.Ordinal),
                    _versions.Count == 0 ? null : [.. _versions]);
            }
        }

        public ProtoTraceValue SnapshotValue()
        {
            lock (_gate)
            {
                return new ProtoTraceValue(
                    id,
                    kind,
                    _name ?? id,
                    _scope,
                    _firstSeenUtc,
                    _lastSeenUtc,
                    [.. _versions]);
            }
        }
    }
}
