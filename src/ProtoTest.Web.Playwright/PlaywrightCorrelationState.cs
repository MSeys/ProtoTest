namespace ProtoTest.Web.Playwright;

using System.Collections.Concurrent;

/// <summary>
/// The session's operation correlation state: which operations are open, which of them own a native
/// trace group, and the most recently begun operation that is still open. Page-level diagnostics
/// (console, page error, failed request) carry no operation identity, so an event that arrives while
/// several operations are open is attributed to the most recently begun one; that is deterministic, and
/// the trace still lists every open operation separately. One type owns the state machine instead of a
/// gate and three collections scattered through the backend.
/// </summary>
internal sealed class PlaywrightCorrelationState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _openOperations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _openTraceGroups = new(StringComparer.Ordinal);
    private long _sequence;

    /// <summary>Serializes native trace group begin/end; only a group-owning operation holds it.</summary>
    public SemaphoreSlim TraceGroupGate { get; } = new(1, 1);

    /// <summary>
    /// The most recently begun operation that is still open, or null when none is. Closing a nested
    /// operation while its parent is open falls back to the parent instead of forgetting the lineage.
    /// </summary>
    public string? Latest
    {
        get
        {
            lock (_gate)
            {
                return _openOperations.Count == 0
                    ? null
                    : _openOperations.MaxBy(pair => pair.Value).Key;
            }
        }
    }

    public bool IsOpen(string correlationId)
    {
        lock (_gate)
        {
            return _openOperations.ContainsKey(correlationId);
        }
    }

    public void Open(string correlationId)
    {
        lock (_gate)
        {
            _openOperations[correlationId] = ++_sequence;
        }
    }

    public void Close(string correlationId)
    {
        lock (_gate)
        {
            _openOperations.Remove(correlationId);
        }
    }

    public void OpenTraceGroup(string correlationId) => _openTraceGroups[correlationId] = 0;

    /// <summary>Claims the group end; false when this operation never owned one.</summary>
    public bool CloseTraceGroup(string correlationId) => _openTraceGroups.TryRemove(correlationId, out _);
}
