namespace ProtoTest.Reporting;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class JsonReportSink : IProtoSink
{
    private readonly JsonReportSinkOptions _options;
    private readonly IConfiguration? _configuration;

    public JsonReportSink() : this(new JsonReportSinkOptions()) { }

    public JsonReportSink(JsonReportSinkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public JsonReportSink(IConfiguration configuration) : this()
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public string OutputPath
    {
        get => _options.OutputPath;
        set => _options.OutputPath = value;
    }

    public bool Indented
    {
        get => _options.Indented;
        set => _options.Indented = value;
    }

    public async Task ExportAsync(
        IEnumerable<ProtoReportItem> items,
        CancellationToken cancellationToken = default)
    {
        _configuration?.GetSection(JsonReportSinkOptions.ConfigurationSectionName).Bind(_options);
        var report = ProtoReport.Create(items);
        var outputPath = Path.GetFullPath(_options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var serializerOptions = new JsonSerializerOptions { WriteIndented = _options.Indented };
        serializerOptions.Converters.Add(new JsonStringEnumConverter());

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, report, serializerOptions, cancellationToken);
    }
}
