namespace ProtoTest.Core;

public sealed class ProtoTraceOptions
{
    /// <summary>Enables automatic trace collection and export.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional output file. When omitted, Core writes TestResults/prototest-{runId}.prototrace.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Names of activity sources whose spans are captured into the trace, such as an application's own
    /// instrumentation or any OpenTelemetry-aware library. Nothing ProtoTest-specific is required of them.
    /// </summary>
    public IList<string> ActivitySources { get; } = new List<string>();
}
