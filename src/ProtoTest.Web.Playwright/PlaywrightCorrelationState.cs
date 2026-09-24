namespace ProtoTest.Web.Playwright;

using System.Collections.Concurrent;

/// <summary>
/// The session's operation correlation state: which operations are open, which of them own a native
/// trace group, and the most recently begun operation. Page-level diagnostics (console, page error,
/// failed request) carry no operation identity, so an event that arrives while several operations are
/// open is attributed to the most recently begun one; that is deterministic, and the trace still lists
/// every open operation separately. One type owns the state machine instead of a gate and three
/// collections scattered through the backend.
/// </summary>
internal sealed class PlaywrightCorrelationState
{
    private readonly ConcurrentDictionary<string, byte> _openOperations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _openTraceGroups = new(StringComparer.Ordinal);
    private string? _latest;

    /// <summary>Serializes native trace group begin/end; only a group-owning operation holds it.</summary>
    public SemaphoreSlim TraceGroupGate { get; } = new(1, 1);

    /// <summary>The most recently begun operation, or null when none is open.</summary>
    public string? Latest => Volatile.Read(ref _latest);

    public bool IsOpen(string correlationId) => _openOperations.ContainsKey(correlationId);

    public void Open(string correlationId)
    {
        _openOperations[correlationId] = 0;
        Volatile.Write(ref _latest, correlationId);
    }

    public void Close(string correlationId)
    {
        _openOperations.TryRemove(correlationId, out _);
        if (string.Equals(Latest, correlationId, StringComparison.Ordinal))
        {
            Volatile.Write(ref _latest, null);
        }
    }

    public void OpenTraceGroup(string correlationId) => _openTraceGroups[correlationId] = 0;

    /// <summary>Claims the group end; false when this operation never owned one.</summary>
    public bool CloseTraceGroup(string correlationId) => _openTraceGroups.TryRemove(correlationId, out _);
}
