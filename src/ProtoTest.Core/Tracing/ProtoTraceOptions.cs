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

    /// <summary>
    /// Records where in the suite's code each operation started (<c>code.file.path</c>, <c>code.line.number</c>,
    /// <c>code.function.name</c>), read from the stack and the suite's symbols. On by default.
    /// </summary>
    public bool CaptureSourceLocations { get; set; } = true;

    /// <summary>
    /// Embeds the source files those locations point at in the trace, so the viewer can show the code around
    /// each operation. Turn it off when a trace leaves the people who may read the suite's code. On by default.
    /// </summary>
    public bool EmbedSources { get; set; } = true;

    /// <summary>
    /// The largest attachment the archive embeds, in bytes. An attachment over the limit is recorded as
    /// an error artifact (its name and size stay visible, its content is left out) instead of being held
    /// in memory or written. Defaults to 64 MB.
    /// </summary>
    public long MaxArtifactBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Embeds attachment content in the archive. Turn it off for a size- or privacy-conscious run: the
    /// attachment is still declared (name, media type, size) but its content is neither read nor written,
    /// and the artifact carries an error saying embedding is disabled. On by default.
    /// </summary>
    public bool EmbedArtifacts { get; set; } = true;
}
