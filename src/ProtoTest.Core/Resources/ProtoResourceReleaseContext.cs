namespace ProtoTest.Core;

/// <summary>Provides everything a resource needs while it is being released.</summary>
public sealed class ProtoResourceReleaseContext
{
    internal ProtoResourceReleaseContext(
        ProtoExecutionContext test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase,
        CancellationToken cancellationToken)
    {
        Test = test ?? throw new ArgumentNullException(nameof(test));
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Phase = phase;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the execution context of the test that owns the resource.</summary>
    public ProtoExecutionContext Test { get; }

    /// <summary>Gets the trace writer so a release can record its own operations and observations.</summary>
    public IProtoTraceWriter Trace { get; }

    /// <summary>Gets the phase the release belongs to, so nested operations are labelled correctly.</summary>
    public ProtoTracePhase Phase { get; }

    /// <summary>Gets the cancellation token for the release.</summary>
    public CancellationToken CancellationToken { get; }
}
