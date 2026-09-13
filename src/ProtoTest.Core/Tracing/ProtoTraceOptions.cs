namespace ProtoTest.Core;

public sealed class ProtoTraceOptions
{
    /// <summary>Enables automatic trace collection and export.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional output file. When omitted, Core writes TestResults/prototest-{runId}.prototrace.
    /// </summary>
    public string? OutputPath { get; set; }
}
