namespace ProtoTest.Core;

/// <summary>
/// A lightweight fluent descriptor for a traced operation. Create one with
/// <see cref="ProtoTraceWriterExtensions.Operation(IProtoTraceWriter, string, string, string)"/>,
/// refine it with <see cref="During"/>, <see cref="Parent"/> and <see cref="With(string, string?)"/>,
/// then either run traced work through <see cref="RunAsync(Func{ValueTask})"/> or open a handle with
/// <see cref="Begin"/>.
/// </summary>
public readonly struct ProtoTraceScope
{
    private readonly IProtoTraceWriter _trace;
    private readonly string _kind;
    private readonly string _name;
    private readonly string _source;
    private readonly ProtoTracePhase _phase;
    private readonly IReadOnlyDictionary<string, string?>? _attributes;
    private readonly string? _parentId;

    internal ProtoTraceScope(
        IProtoTraceWriter trace,
        string kind,
        string name,
        string source,
        ProtoTracePhase phase,
        IReadOnlyDictionary<string, string?>? attributes,
        string? parentId)
    {
        _trace = trace;
        _kind = kind;
        _name = name;
        _source = source;
        _phase = phase;
        _attributes = attributes;
        _parentId = parentId;
    }

    /// <summary>Sets the lifecycle phase requested for the operation.</summary>
    public ProtoTraceScope During(ProtoTracePhase phase)
        => new(_trace, _kind, _name, _source, phase, _attributes, _parentId);

    /// <summary>Sets an explicit parent operation id instead of the ambient parent.</summary>
    public ProtoTraceScope Parent(string? parentId)
        => new(_trace, _kind, _name, _source, _phase, _attributes, parentId);

    /// <summary>Adds a single attribute to the operation.</summary>
    public ProtoTraceScope With(string name, string? value)
        => new(_trace, _kind, _name, _source, _phase, Merge(_attributes, name, value), _parentId);

    /// <summary>Adds a set of attributes to the operation. A null or empty set is ignored.</summary>
    public ProtoTraceScope With(IReadOnlyDictionary<string, string?>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return this;
        }

        var merged = _attributes is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(_attributes, StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            merged[key] = value;
        }

        return new(_trace, _kind, _name, _source, _phase, merged, _parentId);
    }

    /// <summary>
    /// Opens the operation so the caller manages its outcome. The returned handle is tied to the
    /// ambient trace and must be completed within the current operation scope.
    /// </summary>
    public ProtoTraceOperation Begin()
        => _trace.StartOperation(_kind, _name, _source, _phase, _attributes, _parentId);

    /// <summary>Runs <paramref name="action"/> as a traced operation, deriving its outcome.</summary>
    public ValueTask RunAsync(Func<ValueTask> action)
        => _trace.ExecuteAsync(_kind, _name, _source, action, _phase, _attributes, _parentId);

    /// <summary>Runs <paramref name="action"/> as a traced operation, returning its result on success.</summary>
    public ValueTask<TResult> RunAsync<TResult>(Func<ValueTask<TResult>> action)
        => _trace.ExecuteAsync(_kind, _name, _source, action, _phase, _attributes, _parentId);

    /// <summary>Runs <paramref name="action"/> as a traced operation, passing the live operation handle.</summary>
    public ValueTask RunAsync(Func<ProtoTraceOperation, ValueTask> action)
        => _trace.ExecuteAsync(_kind, _name, _source, action, _phase, _attributes, _parentId);

    /// <summary>Runs <paramref name="action"/> as a traced operation, passing the live handle and returning its result.</summary>
    public ValueTask<TResult> RunAsync<TResult>(Func<ProtoTraceOperation, ValueTask<TResult>> action)
        => _trace.ExecuteAsync(_kind, _name, _source, action, _phase, _attributes, _parentId);

    private static IReadOnlyDictionary<string, string?> Merge(
        IReadOnlyDictionary<string, string?>? existing,
        string name,
        string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var merged = existing is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(existing, StringComparer.Ordinal);
        merged[name] = value;
        return merged;
    }
}
