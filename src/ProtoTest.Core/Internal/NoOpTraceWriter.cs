namespace ProtoTest.Core.Internal;

/// <summary>
/// Swallows trace writes. Run-scoped work happens after the trace archive is written, so it has no
/// archive to write to, but release callbacks can still call the writer unconditionally.
/// </summary>
internal sealed class NoOpTraceWriter : IProtoTraceWriter
{
    public static NoOpTraceWriter Instance { get; } = new();

    public ProtoTraceOperation StartOperation(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
        => new();

    public void WriteEvent(
        string kind,
        string name,
        string source,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        ProtoTraceOutcome outcome = ProtoTraceOutcome.Unknown,
        IReadOnlyDictionary<string, string?>? attributes = null,
        Exception? exception = null,
        string? parentId = null)
    {
    }
}
