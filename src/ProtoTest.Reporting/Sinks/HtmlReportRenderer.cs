namespace ProtoTest.Reporting;

using ProtoTest.Core;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// Builds the self-contained report document. The sink owns the file and the options; the markup, the
/// sections and the client assets live here and in <see cref="HtmlReportAssets"/>.
/// </summary>
internal static class HtmlReportRenderer
{
    internal static string Render(ProtoReport report, string title)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="en" data-theme="dark">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="dark light">
            """);
        html.Append("<meta name=\"theme-color\" content=\"#126AA8\"><link rel=\"icon\" type=\"image/svg+xml\" href=\"")
            .Append(HtmlReportAssets.Favicon)
            .Append("\">");
        html.Append("<title>").Append(Encode(title)).Append("</title>");
        html.Append(HtmlReportAssets.Styles);
        html.Append("""
            </head>
            <body>
            <header class="topbar">
              <div class="brand">
                <span class="brand-mark" aria-hidden="true">
            """);
        html.Append(HtmlReportAssets.Mark);
        html.Append("""
                </span>
                <span><strong>ProtoTest</strong><small>REPORTING BLUEPRINT</small></span>
              </div>
              <div class="run-state">
            """);

        var stateClass = report.Summary.Errors > 0 || report.Summary.Uncovered > 0 || report.Summary.Warnings > 0
            ? "attention"
            : "healthy";
        var stateText = report.Summary.Errors > 0
            ? $"{report.Summary.Errors} error{PluralSuffix(report.Summary.Errors)}"
            : report.Summary.Uncovered > 0
                ? $"{report.Summary.Uncovered} uncovered"
                : report.Summary.Warnings > 0
                    ? $"{report.Summary.Warnings} warning{PluralSuffix(report.Summary.Warnings)}"
                    : report.Summary.CoverageTotal > 0
                        ? "All covered"
                        : "Clean run";
        html.Append("<span class=\"state-pill ").Append(stateClass).Append("\"><i></i>")
            .Append(Encode(stateText)).Append("</span></div>");
        html.Append("""
              <div class="top-actions">
                <label class="search"><span aria-hidden="true">⌕</span><input id="reportSearch" type="search" placeholder="Search report…" autocomplete="off"><kbd>/</kbd></label>
                <button class="icon-button" id="themeToggle" type="button" aria-label="Toggle color theme" title="Toggle color theme"><svg class="icon-sun" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="3"/><path d="M8 1.5v1.6M8 12.9v1.6M1.5 8h1.6M12.9 8h1.6M3.4 3.4l1.1 1.1M11.5 11.5l1.1 1.1M3.4 12.6l1.1-1.1M11.5 4.5l1.1-1.1"/></svg><svg class="icon-moon" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M13.2 10.2A5.5 5.5 0 0 1 5.8 2.8a5.5 5.5 0 1 0 7.4 7.4Z"/></svg></button>
              </div>
            </header>
            <main>
              <section class="hero">
                <div>
                  <p class="eyebrow">RUN REPORT</p>
            """);
        html.Append("<h1>").Append(Encode(title)).Append("</h1>");
        html.Append("<p class=\"generated\">Generated <time datetime=\"")
            .Append(report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("\">")
            .Append(Encode(report.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</time></p></div>");

        if (report.Summary.CoverageTotal > 0)
        {
            html.Append("<div class=\"coverage-ring\" style=\"--coverage:")
                .Append(report.Summary.CoveragePercentage.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("\"><span>")
                .Append(report.Summary.CoveragePercentage.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("<small>%</small></span><em>coverage</em></div>");
        }

        html.Append("</section><section class=\"metrics\">");
        SummaryCard(html, "Covered", report.Summary.Covered,
            $"of {report.Summary.CoverageTotal} coverage {(report.Summary.CoverageTotal == 1 ? "entry" : "entries")}", "success");
        SummaryCard(html, "Uncovered", report.Summary.Uncovered, "Coverage gaps", "danger");
        SummaryCard(html, "Occurrences", report.Summary.TotalOccurrences, "Observed hits", "accent");
        SummaryCard(html, "Findings", report.Summary.Findings, "Recorded by tests", "warning");
        SummaryCard(html, "Run gates", report.Summary.Gates, "Run verdicts", GateTone(report.Items));
        SummaryCard(html, "Resources", report.Summary.Resources, "Owned by tests", "accent");
        SummaryCard(html, "Errors", report.Summary.Errors, "Failed entries", "danger");
        html.Append("""
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

        RenderSections(html, report.Items);

        html.Append("""
              </div>
              <div class="empty-state" id="emptyState" hidden><strong>No matching entries</strong><span>Try another search or filter.</span></div>
            </section>
            </main>
            """);
        html.Append("<script>").Append(HtmlReportAssets.Script).Append("</script>");
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void RenderItem(
        StringBuilder html,
        ProtoReportItem item,
        bool isRoot,
        int depth)
    {
        var statusClass = item.Status.ToString().ToLowerInvariant();
        var descendantItems = item.Flatten().ToArray();
        var coverageState = GetCoverageState(descendantItems);
        var coverageClass = coverageState switch
        {
            CoverageState.Covered => "covered",
            CoverageState.Partial => "partial",
            CoverageState.Uncovered => "uncovered",
            _ => "not-applicable"
        };
        var filterTokens = GetFilterTokens(descendantItems, coverageState);
        var searchableText = string.Join(' ', descendantItems.SelectMany(entry => new[]
        {
            entry.Identifier,
            entry.TargetName,
            entry.Category,
            entry.Kind,
            entry.Message ?? string.Empty,
            entry.Tags is null ? string.Empty : string.Join(' ', entry.Tags)
        })).ToLowerInvariant();

        var hasDetails = HasDetails(item);
        var container = hasDetails ? "details" : "article";

        html.Append('<').Append(container).Append(" class=\"report-item ").Append(coverageClass).Append(' ')
            .Append("status-").Append(statusClass).Append(isRoot ? " root" : " child")
            .Append("\" data-report-item=\"").Append(isRoot ? "root" : "child").Append('"');
        if (isRoot)
        {
            html.Append(" data-search=\"").Append(Encode(searchableText)).Append("\" data-filters=\"")
                .Append(filterTokens).Append('"');
        }
        if (hasDetails && depth < 1) html.Append(" open");
        html.Append('>');

        html.Append(hasDetails ? "<summary>" : "<div class=\"item-summary\">");
        html.Append(hasDetails
                ? HtmlReportAssets.Expander
                : "<span class=\"chevron-placeholder\" aria-hidden=\"true\"></span>")
            .Append("<span class=\"status-dot\" aria-hidden=\"true\"></span>")
            .Append("<span class=\"item-heading\"><strong");
        var displayIdentifier = GetDisplayIdentifier(item);
        if (!string.Equals(displayIdentifier, item.Identifier, StringComparison.Ordinal))
        {
            html.Append(" title=\"").Append(Encode(item.Identifier)).Append('"');
        }
        html.Append('>').Append(Encode(displayIdentifier)).Append("</strong>")
            .Append("<span class=\"item-context\">").Append(Encode(GetContextLabel(item, isRoot)))
            .Append("</span></span>")
            .Append("<span class=\"item-badges\">");

        if (coverageState != CoverageState.NotApplicable)
        {
            html.Append("<span class=\"badge coverage-badge\">")
                .Append(coverageState).Append("</span>");
        }
        if (item.IsKind(ProtoReportItemKinds.Gate) || item.Status != ProtoReportStatus.Neutral)
        {
            html.Append("<span class=\"badge status-badge\">").Append(Encode(GetStatusLabel(item))).Append("</span>");
        }
        if (item.Count > 0 && (item.IsKind(ProtoReportItemKinds.Coverage) || item.IsKind(ProtoReportItemKinds.Observation)))
        {
            html.Append("<span class=\"badge\">").Append(item.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" occurrence").Append(PluralSuffix(item.Count)).Append("</span>");
        }
        html.Append(hasDetails ? "</span></summary><div class=\"item-body\">" : "</span></div>");

        if (!hasDetails)
        {
            html.Append("</article>");
            return;
        }

        if (item.Value is not null)
        {
            html.Append("<div class=\"measured-value\"><strong>")
                .Append(item.Value.Value.ToString(CultureInfo.InvariantCulture)).Append("</strong><span>")
                .Append(Encode(item.Unit)).Append("</span></div>");
        }
        if (!string.IsNullOrWhiteSpace(item.Message))
        {
            html.Append("<p class=\"message\">").Append(Encode(item.Message)).Append("</p>");
        }
        if (item.Tags is { Count: > 0 })
        {
            html.Append("<div class=\"tags\">");
            foreach (var tag in item.Tags)
            {
                html.Append("<span class=\"tag\">#").Append(Encode(tag)).Append("</span>");
            }
            html.Append("</div>");
        }
        if (item.Metadata is { Count: > 0 })
        {
            html.Append("<details class=\"metadata\"><summary>Metadata</summary><pre>")
                .Append(Encode(JsonSerializer.Serialize(item.Metadata, new JsonSerializerOptions { WriteIndented = true })))
                .Append("</pre></details>");
        }
        if (item.Children is { Count: > 0 })
        {
            html.Append("<div class=\"children\">");
            foreach (var child in item.Children)
            {
                RenderItem(html, child, isRoot: false, depth + 1);
            }
            html.Append("</div>");
        }
        html.Append("</div></details>");
    }

    private static bool HasDetails(ProtoReportItem item)
        => item.Value is not null
            || !string.IsNullOrWhiteSpace(item.Message)
            || item.Tags is { Count: > 0 }
            || item.Metadata is { Count: > 0 }
            || item.Children is { Count: > 0 };

    private static string GetDisplayIdentifier(ProtoReportItem item)
        => item.DisplayName ?? item.Identifier;

    /// <summary>
    /// Gates speak their own language: a gate does not succeed or error, it passes, advises or fails.
    /// </summary>
    private static string GetStatusLabel(ProtoReportItem item)
        => !item.IsKind(ProtoReportItemKinds.Gate)
            ? item.Status.ToString()
            : item.Status switch
            {
                ProtoReportStatus.Success => "Passed",
                ProtoReportStatus.Warning => "Warning",
                ProtoReportStatus.Error => "Failed",
                ProtoReportStatus.Info => "Info",
                _ => "Skipped"
            };

    private static string GetContextLabel(ProtoReportItem item, bool isRoot)
    {
        if (item.IsKind(ProtoReportItemKinds.Gate))
        {
            return "Run gate";
        }

        if (item.IsKind(ProtoReportItemKinds.Finding))
        {
            return item.DisplayGroup is { Length: > 0 }
                ? $"{item.Category} · {item.DisplayGroup}"
                : item.Category;
        }

        var category = item.DisplayGroup ?? item.Category;
        return isRoot ? $"{item.TargetName} · {category}" : category;
    }

    /// <summary>
    /// Report items are different things, so the report keeps them in different categories rather than
    /// one undifferentiated list. Integrations can add kinds; unknown kinds still get a section, titled
    /// after the kind, rather than disappearing.
    /// </summary>
    private static readonly (string Kind, string Title)[] Sections =
    [
        (ProtoReportItemKinds.Coverage, "Coverage"),
        (ProtoReportItemKinds.Finding, "Findings"),
        (ProtoReportItemKinds.Gate, "Run gates"),
        (ProtoReportItemKinds.Resource, "Resources"),
        (ProtoReportItemKinds.Metric, "Metrics"),
        (ProtoReportItemKinds.Observation, "Observations")
    ];

    private static void RenderSections(StringBuilder html, IReadOnlyList<ProtoReportItem> items)
    {
        foreach (var (kind, title) in Sections)
        {
            RenderSection(
                html,
                items.Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToArray(),
                kind,
                title);
        }

        var known = new HashSet<string>(Sections.Select(section => section.Kind), StringComparer.OrdinalIgnoreCase);
        foreach (var group in items
            .Where(item => !known.Contains(item.Kind))
            .GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase))
        {
            RenderSection(html, [.. group], group.Key, PrettifyKind(group.Key));
        }
    }

    private static void RenderSection(StringBuilder html, IReadOnlyList<ProtoReportItem> roots, string kind, string title)
    {
        if (roots.Count == 0) return;

        html.Append("<section class=\"report-section\" data-report-section data-kind=\"")
            .Append(Encode(kind.ToLowerInvariant()))
            .Append("\"><header class=\"section-head\"><i aria-hidden=\"true\"></i><h3>")
            .Append(Encode(title)).Append("</h3><span class=\"section-count\" data-section-count data-total=\"")
            .Append(roots.Count.ToString(CultureInfo.InvariantCulture)).Append("\">")
            .Append(roots.Count.ToString(CultureInfo.InvariantCulture)).Append(PluralSuffix(roots.Count, " entry", " entries"))
            .Append("</span></header>");

        foreach (var item in roots)
        {
            RenderItem(html, item, isRoot: true, depth: 0);
        }

        html.Append("</section>");
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

    private static string GetFilterTokens(IEnumerable<ProtoReportItem> items, CoverageState coverageState)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (coverageState == CoverageState.Partial)
        {
            tokens.Add("partial");
        }
        foreach (var item in items)
        {
            if (item.IsCovered is true) tokens.Add("covered");
            if (item.IsCovered is false) tokens.Add("uncovered");
            if (item.Status == ProtoReportStatus.Warning) tokens.Add("warning");
            if (item.Status == ProtoReportStatus.Error) tokens.Add("error");
        }

        return string.Join(' ', tokens);
    }

    private static CoverageState GetCoverageState(IEnumerable<ProtoReportItem> items)
    {
        // The shared coverage arithmetic decides what a unit is, so the renderer cannot disagree
        // with the report summary and the run gates.
        var totals = items.CoverageTotals();
        if (totals.Covered > 0 && totals.Uncovered > 0) return CoverageState.Partial;
        if (totals.Covered > 0) return CoverageState.Covered;
        if (totals.Uncovered > 0) return CoverageState.Uncovered;
        return CoverageState.NotApplicable;
    }

    private static void SummaryCard(
        StringBuilder html,
        string label,
        int value,
        string description,
        string tone)
    {
        html.Append("<article class=\"metric ").Append(tone).Append("\"><span>").Append(Encode(label))
            .Append(value == 0 ? "</span><strong data-zero>" : "</span><strong>").Append(value.ToString(CultureInfo.InvariantCulture))
            .Append("</strong><small>").Append(Encode(description)).Append("</small></article>");
    }

    private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";

    private static string PluralSuffix(int count, string singular, string plural)
        => count == 1 ? singular : plural;

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private enum CoverageState
    {
        NotApplicable,
        Covered,
        Partial,
        Uncovered
    }

}
