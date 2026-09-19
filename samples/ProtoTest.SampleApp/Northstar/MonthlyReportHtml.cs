namespace ProtoTest.SampleApp.Northstar;

using System.Net;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;

/// <summary>
/// The monthly report as a printable HTML page, the human counterpart to the OpenXML workbook. It is a
/// report artefact rather than part of the console: the console links to it, a person reads or prints it.
/// </summary>
internal static class MonthlyReportHtml
{
    public static string Render(OrganizationResponse organization, IReadOnlyList<ProjectResponse> projects)
    {
        var rows = string.Concat(projects.Select(project => $"""
                <tr data-testid="report-row">
                  <td data-testid="report-project-name">{WebUtility.HtmlEncode(project.Name)}</td>
                  <td data-testid="report-project-status">{WebUtility.HtmlEncode(project.Status)}</td>
                  <td data-testid="report-project-environments">{project.EnvironmentCount}</td>
                </tr>
            """));
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Northstar monthly report · {{WebUtility.HtmlEncode(organization.Name)}}</title>
              <style>
                :root { color-scheme: light; }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  padding: 48px 24px;
                  background: #eef2f1;
                  color: #16232c;
                  font: 15px/1.5 "Segoe UI", system-ui, -apple-system, sans-serif;
                }
                main { max-width: 760px; margin: 0 auto; background: #fff; border: 1px solid #d9e2e0; border-radius: 12px; padding: 40px; }
                h1 { margin: 0 0 4px; font-size: 24px; letter-spacing: -0.01em; }
                p.meta { margin: 0; color: #5f7180; }
                table { width: 100%; border-collapse: collapse; margin-top: 28px; }
                th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid #e4ebe9; }
                th { font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; color: #5f7180; }
                td:nth-child(3), th:nth-child(3) { text-align: right; }
                footer { margin-top: 28px; color: #5f7180; font-size: 13px; }
                @media print { body { background: #fff; padding: 0; } main { border: 0; border-radius: 0; padding: 0; } }
              </style>
            </head>
            <body>
              <main data-testid="report-html">
                <h1>Monthly project report</h1>
                <p class="meta">
                  <span data-testid="report-org">{{WebUtility.HtmlEncode(organization.Name)}}</span>
                  · generated <time data-testid="report-generated">{{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}} UTC</time>
                </p>
                <table data-testid="report-table">
                  <thead>
                    <tr><th scope="col">Project</th><th scope="col">Status</th><th scope="col">Environments</th></tr>
                  </thead>
                  <tbody>
            {{rows}}
                  </tbody>
                </table>
                <footer>{{projects.Count}} project(s) on the {{WebUtility.HtmlEncode(organization.PlanName)}} plan.</footer>
              </main>
            </body>
            </html>
            """;
    }
}
