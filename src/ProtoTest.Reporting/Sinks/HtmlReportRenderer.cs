namespace ProtoTest.Reporting;

using System.Globalization;
using System.Net;
using System.Text;
using ProtoTest.Core;

/// <summary>
/// Builds the self-contained report document. One instance renders one report: it owns the markup
/// builder and the projected item views, so the methods below read as the document they write. The
/// page follows the ProtoTrace viewer's run view: a headline that says what needs attention, a strip of
/// every entry, one tab per kind of result, and rows. Coverage is one kind among the others.
/// </summary>
internal sealed partial class HtmlReportRenderer
{
    private readonly StringBuilder _html = new();

    public static string Render(ProtoReport report, string title)
        => new HtmlReportRenderer().Build(report, title);

    /// <summary>
    /// Report items are different things, so the report keeps them in different kinds rather than one
    /// undifferentiated list, problems first. Integrations can add kinds; an unknown kind still gets a
    /// tab and a section, titled after the kind, rather than disappearing.
    /// </summary>
    private static readonly (string Kind, string Title, string Tab)[] Sections =
    [
        (ProtoReportItemKinds.Gate, "Run gates", "Gates"),
        (ProtoReportItemKinds.Finding, "Findings", "Findings"),
        (ProtoReportItemKinds.Coverage, "Coverage", "Coverage"),
        (ProtoReportItemKinds.Traffic, "Traffic (observed but unasserted)", "Traffic"),
        (ProtoReportItemKinds.Observation, "Observations", "Observations"),
        (ProtoReportItemKinds.Metric, "Metrics", "Metrics"),
        (ProtoReportItemKinds.Resource, "Resources", "Resources")
    ];

    private string Build(ProtoReport report, string title)
    {
        var groups = Group(ReportViews.Project(report.Items));
        var entries = groups.SelectMany(group => group.Entries).ToArray();
        var attention = entries.Where(entry => entry.Attention is not null).ToArray();

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
                <span><strong>ProtoTest</strong><small>Report</small></span>
              </div>
              <div class="top-actions">
                <label class="search"><span aria-hidden="true">⌕</span><input id="reportSearch" type="search" placeholder="Search report…" aria-label="Search report" autocomplete="off"><kbd>/</kbd></label>
                <button class="icon-button" id="themeToggle" type="button" aria-label="Toggle color theme" title="Toggle color theme"><svg class="icon-sun" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="3"/><path d="M8 1.5v1.6M8 12.9v1.6M1.5 8h1.6M12.9 8h1.6M3.4 3.4l1.1 1.1M11.5 11.5l1.1 1.1M3.4 12.6l1.1-1.1M11.5 4.5l1.1-1.1"/></svg><svg class="icon-moon" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M13.2 10.2A5.5 5.5 0 0 1 5.8 2.8a5.5 5.5 0 1 0 7.4 7.4Z"/></svg></button>
              </div>
            </header>
            <main>
            """);

        RenderSummary(report, title, entries);
        RenderTabs(groups, attention.Length);

        _html.Append("""
            <section class="report-panel">
              <div class="panel-toolbar"><h2 id="viewTitle">All entries</h2><span id="resultCount" aria-live="polite"></span></div>
              <div class="report-list" id="reportList" role="tabpanel">
            """);
        RenderAttention(attention);
        foreach (var group in groups)
        {
            RenderSection(group, report.Summary);
        }

        _html.Append("""
              </div>
              <div class="empty-state" id="emptyState" hidden><strong>No matching entries</strong><span>Try another search or tab.</span></div>
            </section>
            </main>
            <footer class="report-footer"><span>For the full trace, drop the run&apos;s <code>.prototrace</code> file at <a href="https://trace.prototest.dev">trace.prototest.dev</a>.</span></footer>
            """);
        _html.Append("<script>").Append(HtmlReportAssets.Script).Append("</script>");
        _html.Append("</body></html>");
        return _html.ToString();
    }

    /// <summary>The viewer's run header: one sentence that says what needs attention, then the facts.</summary>
    private void RenderSummary(ProtoReport report, string title, IReadOnlyList<ReportEntry> entries)
    {
        _html.Append("<section class=\"summary\"><div class=\"headline\"><h1>");
        var parts = Headline(report, entries);
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0) _html.Append("<span class=\"sep\">, </span>");
            _html.Append("<span class=\"").Append(parts[index].Tone).Append("\">").Append(Encode(parts[index].Text)).Append("</span>");
        }

        _html.Append("</h1><p class=\"meta\"><strong>").Append(Encode(title)).Append("</strong>")
            .Append("<span>Generated <time datetime=\"")
            .Append(report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("\">")
            .Append(Encode(report.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</time></span><span>").Append(ReportText.Count(entries.Count, "entry", "entries")).Append("</span>");
        if (report.Summary.TotalOccurrences > 0)
        {
            _html.Append("<span>").Append(ReportText.Count(report.Summary.TotalOccurrences, "observed occurrence")).Append("</span>");
        }

        _html.Append("</p></div>");
        if (entries.Count > 0)
        {
            _html.Append("<div class=\"strip\" role=\"group\" aria-label=\"")
                .Append(ReportText.Count(entries.Count, "entry", "entries")).Append(", by kind\">");
            foreach (var entry in entries)
            {
                _html.Append("<button type=\"button\" class=\"tick ").Append(entry.Tone).Append("\" data-goto=\"").Append(entry.Id)
                    .Append("\" title=\"").Append(Encode(entry.View.DisplayIdentifier)).Append("\" aria-label=\"")
                    .Append(Encode(entry.View.DisplayIdentifier)).Append("\"></button>");
            }

            _html.Append("</div>");
        }

        _html.Append("</section>");
    }

    private static List<(string Text, string Tone)> Headline(ProtoReport report, IReadOnlyList<ReportEntry> entries)
    {
        var flattened = report.Items.Flatten().ToArray();
        var failedGates = flattened.Count(item => item.IsKind(ProtoReportItemKinds.Gate) && item.Status == ProtoReportStatus.Error);
        var findings = flattened.Count(item => item.IsKind(ProtoReportItemKinds.Finding));
        bool Other(ProtoReportItem item) => !item.IsKind(ProtoReportItemKinds.Gate) && !item.IsKind(ProtoReportItemKinds.Finding);
        var errors = flattened.Count(item => Other(item) && item.Status == ProtoReportStatus.Error);
        var warnings = flattened.Count(item => Other(item) && item.Status == ProtoReportStatus.Warning);

        var parts = new List<(string Text, string Tone)>();
        if (failedGates > 0) parts.Add(($"{ReportText.Count(failedGates, "gate")} failed", "danger"));
        if (errors > 0) parts.Add((ReportText.Count(errors, "error"), "danger"));
        if (report.Summary.Uncovered > 0) parts.Add(($"{report.Summary.Uncovered.ToString(CultureInfo.InvariantCulture)} uncovered", "danger"));
        if (findings > 0) parts.Add((ReportText.Count(findings, "finding"), "warning"));
        if (warnings > 0) parts.Add((ReportText.Count(warnings, "warning"), "warning"));
        if (parts.Count == 0)
        {
            parts.Add((entries.Count == 0 ? "Nothing recorded" : "Nothing needs attention", "success"));
        }

        if (report.Summary.CoverageTotal > 0)
        {
            parts.Add((
                $"{report.Summary.Covered.ToString(CultureInfo.InvariantCulture)} of {ReportText.Count(report.Summary.CoverageTotal, "unit")} covered",
                "muted"));
        }

        return parts;
    }

    /// <summary>The viewer's view tabs: what needs attention first, then one tab per kind, then everything.</summary>
    private void RenderTabs(IReadOnlyList<ReportGroup> groups, int attention)
    {
        _html.Append("<nav class=\"views\" aria-label=\"Report views\"><div class=\"tabs\" role=\"tablist\" aria-label=\"Report views\">");
        if (attention > 0)
        {
            Tab("attention", "Needs attention", attention, "danger");
        }

        foreach (var group in groups)
        {
            Tab(group.Kind, group.Tab, group.Entries.Count, null);
        }

        Tab("all", "All", groups.Sum(group => group.Entries.Count), null);
        _html.Append("</div></nav>");
    }

    private void Tab(string view, string label, int count, string? tone)
    {
        _html.Append("<button type=\"button\" role=\"tab\" class=\"tab\" aria-selected=\"false\" aria-controls=\"reportList\" data-view=\"")
            .Append(Encode(view)).Append("\" data-title=\"").Append(Encode(label)).Append("\">").Append(Encode(label))
            .Append("<span class=\"tab-count").Append(tone is null ? string.Empty : $" {tone}").Append("\">")
            .Append(count.ToString(CultureInfo.InvariantCulture)).Append("</span></button>");
    }

    /// <summary>The viewer's attention list: one row per entry that failed, warned or left a gap.</summary>
    private void RenderAttention(IReadOnlyList<ReportEntry> attention)
    {
        if (attention.Count == 0) return;

        _html.Append("<section class=\"attention\" data-attention>");
        foreach (var entry in attention)
        {
            var (label, tone) = entry.Attention!.Value;
            _html.Append("<button type=\"button\" class=\"issue ").Append(tone).Append("\" data-goto=\"").Append(entry.Id)
                .Append("\" data-search=\"").Append(Encode(entry.Search)).Append("\">")
                .Append("<span class=\"issue-main\"><strong>").Append(Encode(entry.View.DisplayIdentifier)).Append("</strong>")
                .Append("<span class=\"issue-line\"><span class=\"issue-kind\">").Append(Encode(label)).Append("</span>")
                .Append("<span class=\"issue-reason\">").Append(Encode(entry.View.ContextLabel)).Append("</span></span>");
            if (FirstLine(entry.View.Item.Message) is { Length: > 0 } detail)
            {
                _html.Append("<span class=\"issue-detail\">").Append(Encode(detail)).Append("</span>");
            }

            _html.Append("</span></button>");
        }

        _html.Append("</section>");
    }

    private void RenderSection(ReportGroup group, ProtoReportSummary summary)
    {
        _html.Append("<section class=\"report-section\" data-report-section data-kind=\"")
            .Append(Encode(group.Kind))
            .Append("\"><header class=\"section-head\"><i aria-hidden=\"true\"></i><h3>")
            .Append(Encode(group.Title)).Append("</h3>");
        if (string.Equals(group.Kind, ProtoReportItemKinds.Coverage, StringComparison.Ordinal) && summary.CoverageTotal > 0)
        {
            var percentage = summary.CoveragePercentage.ToString("0.##", CultureInfo.InvariantCulture);
            _html.Append("<span class=\"coverage-meter\" title=\"").Append(percentage).Append("% covered\"><i style=\"width:")
                .Append(percentage).Append("%\"></i></span><span class=\"coverage-figure\">")
                .Append(summary.Covered.ToString(CultureInfo.InvariantCulture)).Append(" of ")
                .Append(summary.CoverageTotal.ToString(CultureInfo.InvariantCulture)).Append(" covered · ")
                .Append(percentage).Append("%</span>");
        }

        _html.Append("<span class=\"section-count\" data-section-count data-total=\"")
            .Append(group.Entries.Count.ToString(CultureInfo.InvariantCulture)).Append("\">")
            .Append(ReportText.Count(group.Entries.Count, "entry", "entries"))
            .Append("</span></header>");

        foreach (var entry in group.Entries)
        {
            RenderItem(entry.View, isRoot: true, depth: 0, entry.Id, entry.Search);
        }

        _html.Append("</section>");
    }

    private static List<ReportGroup> Group(IReadOnlyList<ReportItemView> views)
    {
        var groups = new List<ReportGroup>();
        var number = 0;
        void Add(string kind, string title, string tab, IEnumerable<ReportItemView> members)
        {
            var entries = members.Select(view => new ReportEntry(view, $"item-{++number}", Search(view), Attention(view), Tone(view))).ToArray();
            if (entries.Length > 0) groups.Add(new ReportGroup(kind, title, tab, entries));
        }

        foreach (var (kind, title, tab) in Sections)
        {
            Add(kind, title, tab, views.Where(view => string.Equals(view.Item.Kind, kind, StringComparison.OrdinalIgnoreCase)));
        }

        var known = new HashSet<string>(Sections.Select(section => section.Kind), StringComparer.OrdinalIgnoreCase);
        foreach (var group in views
            .Where(view => !known.Contains(view.Item.Kind))
            .GroupBy(view => view.Item.Kind.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var title = PrettifyKind(group.Key);
            Add(group.Key, title, title, group);
        }

        return groups;
    }

    private static (string Label, string Tone)? Attention(ReportItemView view)
    {
        var item = view.Item;
        var statuses = item.Flatten().Select(entry => entry.Status).ToArray();
        var error = statuses.Contains(ProtoReportStatus.Error);
        var warning = statuses.Contains(ProtoReportStatus.Warning);
        if (item.IsKind(ProtoReportItemKinds.Gate))
        {
            return error ? ("Failed gate", "danger") : warning ? ("Gate warning", "warning") : null;
        }

        if (item.IsKind(ProtoReportItemKinds.Finding))
        {
            return ($"{item.Status} finding", error ? "danger" : "warning");
        }

        return view.Coverage switch
        {
            ReportCoverageState.Uncovered => ("Uncovered", "danger"),
            ReportCoverageState.Partial => ("Partly covered", "warning"),
            _ => error ? ("Error", "danger") : warning ? ("Warning", "warning") : null
        };
    }

    private static string Tone(ReportItemView view)
    {
        var statuses = view.Item.Flatten().Select(entry => entry.Status).ToArray();
        if (statuses.Contains(ProtoReportStatus.Error) || view.Coverage == ReportCoverageState.Uncovered) return "danger";
        if (statuses.Contains(ProtoReportStatus.Warning) || view.Coverage == ReportCoverageState.Partial) return "warning";
        return view.Coverage == ReportCoverageState.Covered || view.Item.Status == ProtoReportStatus.Success ? "success" : "neutral";
    }

    private static string Search(ReportItemView view)
        => string.Join(' ', view.Item.Flatten().SelectMany(entry => new[]
        {
            entry.Identifier,
            entry.TargetName,
            entry.Category,
            entry.Kind,
            entry.Message ?? string.Empty,
            entry.Tags is null ? string.Empty : string.Join(' ', entry.Tags)
        })).ToLowerInvariant();

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Split('\n', 2)[0].TrimEnd('\r');
        return line.Length <= 200 ? line : string.Concat(line.AsSpan(0, 200), "…");
    }

    private static string PrettifyKind(string kind)
    {
        var words = kind.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Trim();
        return words.Length == 0 ? kind : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>One root entry: its view, its anchor, its search text, why it needs attention and its tone.</summary>
    private sealed record ReportEntry(
        ReportItemView View,
        string Id,
        string Search,
        (string Label, string Tone)? Attention,
        string Tone);

    /// <summary>The entries of one kind, with the section title and the shorter tab label.</summary>
    private sealed record ReportGroup(string Kind, string Title, string Tab, IReadOnlyList<ReportEntry> Entries);
}
