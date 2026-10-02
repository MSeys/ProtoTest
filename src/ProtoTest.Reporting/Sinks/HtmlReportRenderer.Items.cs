namespace ProtoTest.Reporting;

using System.Globalization;
using System.Text.Json;
using ProtoTest.Core;

/// <summary>The item markup: one projected report item as a collapsible row with its badges, details and children.</summary>
internal sealed partial class HtmlReportRenderer
{
    private void RenderItem(ReportItemView view, bool isRoot, int depth, string? id = null, string? search = null)
    {
        var item = view.Item;
        var statusClass = item.Status.ToString().ToLowerInvariant();
        var container = view.HasDetails ? "details" : "article";

        _html.Append('<').Append(container).Append(" class=\"report-item ").Append(view.CoverageClass).Append(' ')
            .Append("status-").Append(statusClass).Append(isRoot ? " root" : " child")
            .Append("\" data-report-item=\"").Append(isRoot ? "root" : "child").Append('"');
        if (isRoot)
        {
            _html.Append(" id=\"").Append(id).Append("\" data-search=\"").Append(Encode(search)).Append('"');
        }
        if (view.HasDetails && depth < 1) _html.Append(" open");
        _html.Append('>');

        _html.Append(view.HasDetails ? "<summary>" : "<div class=\"item-summary\">");
        _html.Append(view.HasDetails
                ? HtmlReportAssets.Expander
                : "<span class=\"chevron-placeholder\" aria-hidden=\"true\"></span>")
            .Append("<span class=\"status-dot\" aria-hidden=\"true\"></span>")
            .Append("<span class=\"item-heading\"><strong");
        if (!string.Equals(view.DisplayIdentifier, item.Identifier, StringComparison.Ordinal))
        {
            _html.Append(" title=\"").Append(Encode(item.Identifier)).Append('"');
        }
        _html.Append('>').Append(Encode(view.DisplayIdentifier)).Append("</strong>")
            .Append("<span class=\"item-context\">").Append(Encode(view.ContextLabel))
            .Append("</span></span>")
            .Append("<span class=\"item-badges\">");

        if (view.Coverage != ReportCoverageState.NotApplicable)
        {
            _html.Append("<span class=\"badge coverage-badge\">")
                .Append(view.Coverage).Append("</span>");
        }
        if (item.IsKind(ProtoReportItemKinds.Gate) || item.Status != ProtoReportStatus.Neutral)
        {
            _html.Append("<span class=\"badge status-badge\">").Append(Encode(view.StatusLabel)).Append("</span>");
        }
        if (item.Count > 0 && (item.IsKind(ProtoReportItemKinds.Coverage) || item.IsKind(ProtoReportItemKinds.Observation)))
        {
            _html.Append("<span class=\"badge\">").Append(ReportText.Count(item.Count, "occurrence")).Append("</span>");
        }
        _html.Append(view.HasDetails ? "</span></summary><div class=\"item-body\">" : "</span></div>");

        if (!view.HasDetails)
        {
            _html.Append("</article>");
            return;
        }

        if (item.Value is not null)
        {
            _html.Append("<div class=\"measured-value\"><strong>")
                .Append(item.Value.Value.ToString(CultureInfo.InvariantCulture)).Append("</strong><span>")
                .Append(Encode(item.Unit)).Append("</span></div>");
        }
        if (!string.IsNullOrWhiteSpace(item.Message))
        {
            _html.Append("<p class=\"message\">").Append(Encode(item.Message)).Append("</p>");
        }
        if (item.Tags is { Count: > 0 })
        {
            _html.Append("<div class=\"tags\">");
            foreach (var tag in item.Tags)
            {
                _html.Append("<span class=\"tag\">#").Append(Encode(tag)).Append("</span>");
            }
            _html.Append("</div>");
        }
        if (item.Metadata is { Count: > 0 })
        {
            _html.Append("<details class=\"metadata\"><summary>Metadata</summary><pre>")
                .Append(Encode(JsonSerializer.Serialize(item.Metadata, new JsonSerializerOptions { WriteIndented = true })))
                .Append("</pre></details>");
        }
        if (view.Children.Count > 0)
        {
            _html.Append("<div class=\"children\">");
            foreach (var child in view.Children)
            {
                RenderItem(child, isRoot: false, depth + 1);
            }
            _html.Append("</div>");
        }
        _html.Append("</div></details>");
    }
}
