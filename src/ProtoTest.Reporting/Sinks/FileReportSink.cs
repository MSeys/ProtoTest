namespace ProtoTest.Reporting;

using ProtoTest.Core;

/// <summary>Options for a file-based report sink: at minimum, where it writes its output.</summary>
public interface IProtoFileReportOptions
{
    /// <summary>Gets or sets the output path.</summary>
    string OutputPath { get; set; }
}

/// <summary>
/// Base class for sinks that render a report to a single file and expose it as a run artifact.
/// Handles output-path resolution, directory creation, and artifact exposure; subclasses only render.
/// </summary>
public abstract class FileReportSink<TOptions> : IProtoSink, IProtoSinkArtifactSource, IProtoConfigurableOptions
    where TOptions : class, IProtoFileReportOptions
{
    private readonly string _configurationSectionName;
    private string? _lastOutputPath;

    protected FileReportSink(TOptions options, string configurationSectionName)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);
        _configurationSectionName = configurationSectionName;
    }

    /// <summary>Gets the configured options.</summary>
    protected TOptions Options { get; }

    /// <summary>Gets or sets the output path.</summary>
    public string OutputPath
    {
        get => Options.OutputPath;
        set => Options.OutputPath = value;
    }

    string IProtoConfigurableOptions.ConfigurationSectionName => _configurationSectionName;

    /// <summary>Gets the media type of the produced artifact.</summary>
    protected abstract string MediaType { get; }

    /// <summary>Gets the description of the produced artifact.</summary>
    protected abstract string ArtifactDescription { get; }

    /// <summary>Writes the rendered report to <paramref name="outputPath"/>.</summary>
    protected abstract Task WriteAsync(
        string outputPath,
        ProtoReport report,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task ExportAsync(
        IEnumerable<ProtoReportItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var report = ProtoReport.Create(items);
        var outputPath = Path.GetFullPath(Options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await WriteAsync(outputPath, report, cancellationToken).ConfigureAwait(false);
        _lastOutputPath = outputPath;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ProtoTestAttachment> GetArtifacts()
        => _lastOutputPath is null || !File.Exists(_lastOutputPath)
            ? []
            : [ProtoTestAttachment.FromFile(_lastOutputPath, mediaType: MediaType, description: ArtifactDescription)];
}
