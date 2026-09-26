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
    /// Explicit facts about the environment the run executed in - the CI build and run it came from -
    /// recorded with the run as <c>environment.{key}</c> attributes on its trace and as run-metadata
    /// items in its reports. A key must not be empty and must not collide with a built-in environment
    /// fact (<c>runtime</c>, <c>os</c>, <c>processArchitecture</c>, <c>osArchitecture</c>); values are
    /// recorded as-is, so record only what may travel in a trace.
    /// </summary>
    public IDictionary<string, string> RunMetadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Names of environment variables lifted into <see cref="RunMetadata"/>, such as
    /// <c>GITHUB_RUN_ID</c> on GitHub Actions. They are read once, when the host is built; a variable
    /// that is unset or empty contributes nothing, so a local run records no metadata. An explicit
    /// <see cref="RunMetadata"/> entry with the same name wins; a name must not be empty or collide with
    /// a built-in environment fact.
    /// </summary>
    public IList<string> RunMetadataEnvironmentVariables { get; } = new List<string>();

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
