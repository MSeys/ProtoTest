namespace ProtoTest.Reporting;

using System.Globalization;
using System.Net;
using System.Text;
using ProtoTest.Core;

/// <summary>
/// Builds the self-contained report document. One instance renders one report: it owns the markup
/// builder and the projected item views, so the methods below read as the document they write. The
/// markup, the sections and the client assets live here and in <see cref="HtmlReportAssets"/>.
/// </summary>
internal sealed partial class HtmlReportRenderer
{
    private readonly StringBuilder _html = new();

    public static string Render(ProtoReport report, string title)
        => new HtmlReportRenderer().Build(report, title);

    private string Build(ProtoReport report, string title)
    {
        _html.Append("""
            <!doctype html>
            <html lang="en" data-theme="dark">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="dark light">
            """);
        _html.Append("<meta name=\"theme-color\" content=\"#126AA8\"><link rel=\"icon\" type=\"image/svg+xml\" href=\"")
            .Append(HtmlReportAssets.Favicon)
            .Append("\">");
        _html.Append("<title>").Append(Encode(title)).Append("</title>");
        _html.Append(HtmlReportAssets.Styles);
        _html.Append("""
            </head>
            <body>
            <header class="topbar">
              <div class="brand">
                <span class="brand-mark" aria-hidden="true">
            """);
        _html.Append(HtmlReportAssets.Mark);
        _html.Append("""
                </span>
                <span><strong>ProtoTest</strong><small>REPORTING BLUEPRINT</small></span>
              </div>
              <div class="run-state">
            """);

        var stateClass = report.Summary.Errors > 0 || report.Summary.Uncovered > 0 || report.Summary.Warnings > 0
            ? "attention"
            : "healthy";
        var stateText = report.Summary.Errors > 0
            ? ReportText.Count(report.Summary.Errors, "error")
            : report.Summary.Uncovered > 0
                ? $"{report.Summary.Uncovered} uncovered"
                : report.Summary.Warnings > 0
                    ? ReportText.Count(report.Summary.Warnings, "warning")
                    : report.Summary.CoverageTotal > 0
                        ? "All covered"
                        : "Clean run";
        _html.Append("<span class=\"state-pill ").Append(stateClass).Append("\"><i></i>")
            .Append(Encode(stateText)).Append("</span></div>");
        _html.Append("""
              <div class="top-actions">
                <label class="search"><span aria-hidden="true">⌕</span><input id="reportSearch" type="search" placeholder="Search report…" aria-label="Search report" autocomplete="off"><kbd>/</kbd></label>
                <button class="icon-button" id="themeToggle" type="button" aria-label="Toggle color theme" title="Toggle color theme"><svg class="icon-sun" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="3"/><path d="M8 1.5v1.6M8 12.9v1.6M1.5 8h1.6M12.9 8h1.6M3.4 3.4l1.1 1.1M11.5 11.5l1.1 1.1M3.4 12.6l1.1-1.1M11.5 4.5l1.1-1.1"/></svg><svg class="icon-moon" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M13.2 10.2A5.5 5.5 0 0 1 5.8 2.8a5.5 5.5 0 1 0 7.4 7.4Z"/></svg></button>
              </div>
            </header>
            <main>
              <section class="hero">
                <div>
                  <p class="eyebrow">RUN REPORT</p>
            """);
        _html.Append("<h1>").Append(Encode(title)).Append("</h1>");
        _html.Append("<p class=\"generated\">Generated <time datetime=\"")
            .Append(report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("\">")
            .Append(Encode(report.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</time></p></div>");

        if (report.Summary.CoverageTotal > 0)
        {
            _html.Append("<div class=\"coverage-ring\" style=\"--coverage:")
                .Append(report.Summary.CoveragePercentage.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("\"><span>")
                .Append(report.Summary.CoveragePercentage.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("<small>%</small></span><em>coverage</em></div>");
        }

        _html.Append("</section><section class=\"metrics\">");
        SummaryCard("Covered", report.Summary.Covered,
            $"of {ReportText.Count(report.Summary.CoverageTotal, "entry", "entries")}", "success");
        SummaryCard("Uncovered", report.Summary.Uncovered, "Coverage gaps", "danger");
        SummaryCard("Occurrences", report.Summary.TotalOccurrences, "Observed hits", "accent");
        SummaryCard("Findings", report.Summary.Findings, "Recorded by tests", "warning");
        SummaryCard("Run gates", report.Summary.Gates, "Run verdicts", GateTone(report.Items));
        SummaryCard("Resources", report.Summary.Resources, "Owned by tests", "accent");
        SummaryCard("Errors", report.Summary.Errors, "Failed entries", "danger");
        _html.Append("""
            </section>
            <section class="report-panel">
              <div class="panel-toolbar">
                <div><h2>Report details</h2><span id="resultCount" aria-live="polite"></span></div>
                <div class="filters" role="group" aria-label="Filter report">
                  <button class="filter active" type="button" data-filter="all">All</button>
                  <button class="filter" type="button" data-filter="covered">Covered</button>
                  <button class="filter" type="button" data-filter="partial">Partial</button>
                  <button class="filter" type="button" data-filter="uncovered">Uncovered</button>
                  <button class="filter" type="button" data-filter="warning">Warnings</button>
                  <button class="filter" type="button" data-filter="error">Errors</button>
                </div>
              </div>
              <div class="report-list" id="reportList">
            """);

        RenderSections(ReportViews.Project(report.Items));

        _html.Append("""
              </div>
              <div class="empty-state" id="emptyState" hidden><strong>No matching entries</strong><span>Try another search or filter.</span></div>
            </section>
            </main>
            """);
        _html.Append("<script>").Append(HtmlReportAssets.Script).Append("</script>");
        _html.Append("</body></html>");
        return _html.ToString();
    }

    /// <summary>
    /// Report items are different things, so the report keeps them in different categories rather than
    /// one undifferentiated list. Integrations can add kinds; unknown kinds still get a section, titled
    /// after the kind, rather than disappearing.
    /// </summary>
    private static readonly (string Kind, string Title)[] Sections =
    [
        (ProtoReportItemKinds.Coverage, "Coverage"),
        (ProtoReportItemKinds.Traffic, "Traffic (observed but unasserted)"),
        (ProtoReportItemKinds.Finding, "Findings"),
        (ProtoReportItemKinds.Gate, "Run gates"),
        (ProtoReportItemKinds.Resource, "Resources"),
        (ProtoReportItemKinds.Metric, "Metrics"),
        (ProtoReportItemKinds.Observation, "Observations")
    ];

    private void RenderSections(IReadOnlyList<ReportItemView> views)
    {
        foreach (var (kind, title) in Sections)
        {
            RenderSection(
                [.. views.Where(view => string.Equals(view.Item.Kind, kind, StringComparison.OrdinalIgnoreCase))],
                kind,
                title);
        }

        var known = new HashSet<string>(Sections.Select(section => section.Kind), StringComparer.OrdinalIgnoreCase);
        foreach (var group in views
            .Where(view => !known.Contains(view.Item.Kind))
            .GroupBy(view => view.Item.Kind, StringComparer.OrdinalIgnoreCase))
        {
            RenderSection([.. group], group.Key, PrettifyKind(group.Key));
        }
    }

    private void RenderSection(IReadOnlyList<ReportItemView> roots, string kind, string title)
    {
        if (roots.Count == 0) return;

        _html.Append("<section class=\"report-section\" data-report-section data-kind=\"")
            .Append(Encode(kind.ToLowerInvariant()))
            .Append("\"><header class=\"section-head\"><i aria-hidden=\"true\"></i><h3>")
            .Append(Encode(title)).Append("</h3><span class=\"section-count\" data-section-count data-total=\"")
            .Append(roots.Count.ToString(CultureInfo.InvariantCulture)).Append("\">")
            .Append(ReportText.Count(roots.Count, "entry", "entries"))
            .Append("</span></header>");

        foreach (var item in roots)
        {
            RenderItem(item, isRoot: true, depth: 0);
        }

        _html.Append("</section>");
    }

    private static string PrettifyKind(string kind)
    {
        var words = kind.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Trim();
        return words.Length == 0 ? kind : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string GateTone(IEnumerable<ProtoReportItem> items)
    {
        var gates = items.Flatten().Where(item => item.IsKind(ProtoReportItemKinds.Gate)).ToArray();
        if (gates.Length == 0) return "neutral";
        if (gates.Any(item => item.Status == ProtoReportStatus.Error)) return "danger";
        if (gates.Any(item => item.Status == ProtoReportStatus.Warning)) return "warning";
        return "success";
    }

    private void SummaryCard(
        string label,
        int value,
        string description,
        string tone)
    {
        _html.Append("<article class=\"metric ").Append(tone).Append("\"><span>").Append(Encode(label))
            .Append(value == 0 ? "</span><strong data-zero>" : "</span><strong>").Append(value.ToString(CultureInfo.InvariantCulture))
            .Append("</strong><small>").Append(Encode(description)).Append("</small></article>");
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
