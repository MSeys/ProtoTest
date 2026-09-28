namespace ProtoTest.Cli;

using System.Globalization;
using System.Net;
using System.Text;
using ProtoTest.Traces;

/// <summary>
/// Renders the static index page: one table row per run with its outcome counts, the tests that did
/// not pass and links to the trace archive and the digest, plus the archives the scan had to skip.
/// The page carries no timestamp of its own, so the same folder renders the same page.
/// </summary>
internal static class ProtoTraceIndexHtml
{
    private const string Styles = """
:root {
  --paper: #f7f5ee;
  --ink: #183f5b;
  --muted: #657f91;
  --line: #ded8c8;
  --green: #35b985;
  --red: #d86a6a;
  --amber: #d7a84b;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--paper);
  color: var(--ink);
  font: 15px/1.55 Manrope, system-ui, sans-serif;
}
main { max-width: 1120px; margin: 0 auto; padding: 2.5rem clamp(1rem, 4vw, 3rem) 4rem; }
header { display: grid; gap: .35rem; padding-bottom: 1.25rem; border-bottom: 1px solid var(--line); }
h1 { margin: 0; font-family: "Space Grotesk", system-ui, sans-serif; font-size: 1.6rem; letter-spacing: -.01em; }
h2 { margin: 2.5rem 0 .5rem; font-family: "Space Grotesk", system-ui, sans-serif; font-size: 1.1rem; }
p { margin: 0; }
.meta { color: var(--muted); }
.root code, .id, .time { font-family: "JetBrains Mono", ui-monospace, monospace; }
.root code { font-size: .85em; color: var(--muted); }
.scroll { overflow-x: auto; }
table { width: 100%; min-width: 760px; margin-top: 1.5rem; border-collapse: collapse; font-variant-numeric: tabular-nums; }
th { padding: .5rem .75rem .5rem 0; border-bottom: 1px solid var(--ink); text-align: left; font-size: .8rem; font-weight: 600; color: var(--muted); }
td { padding: .7rem .75rem .7rem 0; border-bottom: 1px solid var(--line); vertical-align: top; }
.id { font-size: .85rem; }
.time { font-size: .85rem; color: var(--muted); white-space: nowrap; }
.outcomes, .tests { display: flex; flex-wrap: wrap; gap: .35rem .5rem; }
.outcome { padding: .1rem .55rem .1rem .45rem; border: 1px solid var(--line); border-left-width: 3px; border-radius: 3px; font-size: .8rem; }
.outcome-succeeded { border-left-color: var(--green); background: color-mix(in srgb, var(--green) 10%, var(--paper)); }
.outcome-failed { border-left-color: var(--red); background: color-mix(in srgb, var(--red) 10%, var(--paper)); }
.outcome-partial { border-left-color: var(--amber); background: color-mix(in srgb, var(--amber) 12%, var(--paper)); }
.outcome-other { border-left-color: var(--muted); }
.ok { color: color-mix(in srgb, var(--green) 62%, var(--ink)); }
.links { display: flex; gap: .75rem; white-space: nowrap; }
a { color: var(--ink); text-decoration-color: var(--muted); text-underline-offset: 2px; }
.skipped ul { margin: .5rem 0 0; padding-left: 1.25rem; }
.skipped li { margin: .25rem 0; }
.skipped code { font-family: "JetBrains Mono", ui-monospace, monospace; font-size: .85em; }
""";

    /// <summary>Renders the whole page for one folder scan.</summary>
    public static string Render(string root, IReadOnlyList<IndexedRun> runs, IReadOnlyList<ProtoTraceSkippedArchive> skipped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(skipped);

        var page = new StringBuilder();
        Line(page, "<!doctype html>");
        Line(page, "<html lang=\"en\">");
        Line(page, "<head>");
        Line(page, "<meta charset=\"utf-8\">");
        Line(page, "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        Line(page, "<title>ProtoTest runs</title>");
        Line(page, "<style>");
        Line(page, Styles);
        Line(page, "</style>");
        Line(page, "</head>");
        Line(page, "<body>");
        Line(page, "<main>");
        Line(page, "<header>");
        Line(page, "<h1>ProtoTest runs</h1>");
        Line(page, $"<p class=\"root\"><code>{Encode(root)}</code></p>");
        Line(page, $"<p class=\"meta\">{Runs(runs.Count)}, newest first. Each run links its trace archive and its diagnosis digest.</p>");
        Line(page, "</header>");
        Line(page, "<div class=\"scroll\">");
        Line(page, "<table>");
        Line(page, "<thead><tr><th scope=\"col\">Run</th><th scope=\"col\">Started</th><th scope=\"col\">Completed</th><th scope=\"col\">Outcomes</th><th scope=\"col\">Tests that did not pass</th><th scope=\"col\">Evidence</th></tr></thead>");
        Line(page, "<tbody>");
        foreach (var run in runs)
        {
            AppendRun(page, root, run);
        }

        Line(page, "</tbody>");
        Line(page, "</table>");
        Line(page, "</div>");
        if (skipped.Count > 0)
        {
            AppendSkipped(page, root, skipped);
        }

        Line(page, "</main>");
        Line(page, "</body>");
        Line(page, "</html>");
        return page.ToString();
    }

    private static void AppendRun(StringBuilder page, string root, IndexedRun run)
    {
        var diagnosis = run.Diagnosis;
        page.Append("<tr>");
        page.Append("<td><span class=\"id\">").Append(Encode(diagnosis.RunId)).Append("</span></td>");
        page.Append("<td class=\"time\">").Append(Encode(Timestamp(diagnosis.StartedAtUtc))).Append("</td>");
        page.Append("<td class=\"time\">").Append(Encode(Timestamp(diagnosis.CompletedAtUtc))).Append("</td>");
        page.Append("<td><div class=\"outcomes\">");
        foreach (var (outcome, count) in diagnosis.Outcomes)
        {
            page.Append("<span class=\"outcome ").Append(OutcomeClass(outcome)).Append("\">")
                .Append(count.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Encode(outcome))
                .Append("</span>");
        }

        page.Append("</div></td>");
        page.Append("<td><div class=\"tests\">");
        if (diagnosis.Failures.Count == 0)
        {
            page.Append("<span class=\"ok\">all succeeded</span>");
        }
        else
        {
            foreach (var failure in diagnosis.Failures)
            {
                page.Append("<span><span class=\"id\">").Append(Encode(failure.TestId)).Append("</span> ")
                    .Append(Encode(failure.Name)).Append("</span>");
            }
        }

        page.Append("</div></td>");
        page.Append("<td><div class=\"links\"><a href=\"").Append(Href(root, run.Run.TraceFile)).Append("\">trace</a>")
            .Append("<a href=\"").Append(Href(root, run.DigestPath)).Append("\">digest</a></div></td>");
        Line(page, "</tr>");
    }

    private static void AppendSkipped(StringBuilder page, string root, IReadOnlyList<ProtoTraceSkippedArchive> skipped)
    {
        Line(page, "<section class=\"skipped\">");
        Line(page, "<h2>Not indexed</h2>");
        Line(page, "<p class=\"meta\">The folder holds archives the reader could not open. They are named here with the reason instead of being guessed at.</p>");
        Line(page, "<ul>");
        foreach (var skip in skipped)
        {
            page.Append("<li><code>").Append(Encode(Relative(root, skip.TraceFile))).Append("</code> ")
                .Append(Encode(skip.Reason)).Append("</li>");
            Line(page);
        }

        Line(page, "</ul>");
        Line(page, "</section>");
    }

    private static string Runs(int count)
        => count == 1
            ? "1 run"
            : $"{count.ToString(CultureInfo.InvariantCulture)} runs";

    private static string Timestamp(DateTimeOffset? value)
        => value is null
            ? "not recorded"
            : value.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);

    private static string OutcomeClass(string outcome)
        => outcome switch
        {
            "succeeded" => "outcome-succeeded",
            "failed" => "outcome-failed",
            "partial" => "outcome-partial",
            _ => "outcome-other"
        };

    private static string Href(string root, string path)
        => string.Join('/', Relative(root, path).Split('/').Select(Uri.EscapeDataString));

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static void Line(StringBuilder page, string text = "") => page.Append(text).Append('\n');
}
