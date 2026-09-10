namespace ProtoTest.Reporting;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

public sealed class HtmlReportSink : IProtoSink
{
    private readonly HtmlReportSinkOptions _options;
    private readonly IConfiguration? _configuration;

    public HtmlReportSink() : this(new HtmlReportSinkOptions()) { }

    public HtmlReportSink(HtmlReportSinkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public HtmlReportSink(IConfiguration configuration) : this()
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public string OutputPath
    {
        get => _options.OutputPath;
        set => _options.OutputPath = value;
    }

    public string Title
    {
        get => _options.Title;
        set => _options.Title = value;
    }

    public async Task ExportAsync(
        IEnumerable<ProtoReportItem> items,
        CancellationToken cancellationToken = default)
    {
        _configuration?.GetSection(HtmlReportSinkOptions.ConfigurationSectionName).Bind(_options);
        var outputPath = Path.GetFullPath(_options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            Render(ProtoReport.Create(items)),
            Encoding.UTF8,
            cancellationToken);
    }

    private string Render(ProtoReport report)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            """);
        html.Append("<title>").Append(Encode(_options.Title)).Append("</title>");
        html.Append("""
            <style>
            :root{color-scheme:light dark;font-family:system-ui,sans-serif}body{max-width:1100px;margin:2rem auto;padding:0 1rem}
            .summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:.75rem;margin:1.5rem 0}
            .card,.item{border:1px solid #8886;border-radius:.6rem;padding:.8rem}.value{font-size:1.5rem;font-weight:700}
            ul{list-style:none;padding-left:1.25rem}.item{margin:.5rem 0;border-left:5px solid #777}.covered{border-left-color:#2e9d55}
            .uncovered{border-left-color:#c33}.status-info{background:#2780e322}.status-warning{background:#e6a7002b}.status-error{background:#d33a}
            .meta{opacity:.75;font-size:.9rem}.tag{display:inline-block;border:1px solid #8888;border-radius:1rem;padding:.1rem .5rem;margin:.2rem}
            </style></head><body>
            """);
        html.Append("<h1>").Append(Encode(_options.Title)).Append("</h1>");
        html.Append("<p class=meta>Generated ").Append(Encode(report.GeneratedAtUtc.ToString("u"))).Append("</p>");
        html.Append("<section class=summary>");
        if (report.Summary.CoverageTotal > 0)
        {
            SummaryCard(html, "Coverage", $"{report.Summary.CoveragePercentage.ToString("0.##", CultureInfo.InvariantCulture)}%");
        }
        SummaryCard(html, "Items", report.Summary.Total.ToString(CultureInfo.InvariantCulture));
        SummaryCard(html, "Occurrences", report.Summary.TotalOccurrences.ToString(CultureInfo.InvariantCulture));
        SummaryCard(html, "Warnings", report.Summary.Warnings.ToString(CultureInfo.InvariantCulture));
        SummaryCard(html, "Errors", report.Summary.Errors.ToString(CultureInfo.InvariantCulture));
        html.Append("</section><ul>");
        foreach (var item in report.Items) RenderItem(html, item);
        html.Append("</ul></body></html>");
        return html.ToString();
    }

    private static void RenderItem(StringBuilder html, ProtoReportItem item)
    {
        var coverageClass = item.IsCovered switch
        {
            true => "covered",
            false => "uncovered",
            null => string.Empty
        };
        var statusClass = $"status-{item.Status.ToString().ToLowerInvariant()}";
        html.Append("<li class=\"item ").Append(coverageClass).Append(' ').Append(statusClass).Append("\">");
        html.Append("<strong>").Append(Encode(item.Identifier)).Append("</strong>");
        html.Append(" <span class=meta>").Append(Encode(item.TargetName)).Append(" · ")
            .Append(Encode(item.Category)).Append(" · ").Append(Encode(item.Kind.ToString()));
        if (item.Count > 0) html.Append(" · ").Append(item.Count).Append(" occurrence(s)");
        html.Append("</span>");

        if (item.Value is not null)
            html.Append("<p class=value>").Append(item.Value.Value.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Encode(item.Unit)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(item.Message)) html.Append("<p>").Append(Encode(item.Message)).Append("</p>");
        if (item.Tags is not null)
            foreach (var tag in item.Tags) html.Append("<span class=tag>").Append(Encode(tag)).Append("</span>");
        if (item.Metadata is { Count: > 0 })
            html.Append("<details><summary>Metadata</summary><pre>")
                .Append(Encode(JsonSerializer.Serialize(item.Metadata, new JsonSerializerOptions { WriteIndented = true })))
                .Append("</pre></details>");
        if (item.Children is { Count: > 0 })
        {
            html.Append("<ul>");
            foreach (var child in item.Children) RenderItem(html, child);
            html.Append("</ul>");
        }
        html.Append("</li>");
    }

    private static void SummaryCard(StringBuilder html, string label, string value)
        => html.Append("<div class=card><div class=value>").Append(Encode(value))
            .Append("</div><div>").Append(Encode(label)).Append("</div></div>");

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
