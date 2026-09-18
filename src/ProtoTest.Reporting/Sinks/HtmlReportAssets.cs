namespace ProtoTest.Reporting;

using System.Text;

/// <summary>
/// The report's static assets. Every one is embedded, so a report is a single file that makes no requests:
/// the shared token sheet, the report's own stylesheet, the tiny script that runs the filters and theme, and
/// the brand mark. Newlines are normalised on read so the emitted script has the same bytes on every
/// platform — which is what lets the viewer's content-security policy carry its hash.
/// </summary>
internal static class HtmlReportAssets
{
    internal static readonly string Mark = Read("ProtoTest.Reporting.BrandMark.svg");
    internal static readonly string Tokens = Read("ProtoTest.Reporting.Tokens.css");
    internal static readonly string Sheet = Read("ProtoTest.Reporting.Report.css");
    internal static readonly string Script = Read("ProtoTest.Reporting.Report.js");

    /// <summary>The one shared token file first, then the report's sheet, so the report cannot drift.</summary>
    internal static readonly string Styles = $"<style>{Tokens}{Sheet}</style>";

    /// <summary>
    /// The inline mark takes its colours from the page's surface tokens. A favicon is an image and cannot see
    /// them, so it carries its own light and dark fills and follows the operating system instead.
    /// </summary>
    internal static readonly string Favicon = "data:image/svg+xml;base64," + Convert.ToBase64String(
        Encoding.UTF8.GetBytes(ThemedForImage(Mark)));

    /// <summary>The viewer's expander: a plus that loses its stem when the row is open.</summary>
    internal const string Expander =
        "<span class=\"chevron\" aria-hidden=\"true\"><svg viewBox=\"0 0 10 10\" width=\"10\" height=\"10\">"
        + "<path d=\"M1.6 5H8.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\"/>"
        + "<path class=\"stem\" d=\"M5 1.6V8.4\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\"/>"
        + "</svg></span>";

    private static string ThemedForImage(string svg)
    {
        const string style = "<style>path:nth-of-type(1){fill:#123B58}path:nth-of-type(2){fill:#1688BF}"
            + "@media (prefers-color-scheme: dark){path:nth-of-type(1){fill:#F7F5EE}path:nth-of-type(2){fill:#19A8B5}}</style>";
        var tagEnd = svg.IndexOf('>', svg.IndexOf("<svg", StringComparison.Ordinal));
        return tagEnd < 0 ? svg : svg.Insert(tagEnd + 1, style);
    }

    private static string Read(string resourceName)
    {
        using var stream = typeof(HtmlReportAssets).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded reporting asset '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
