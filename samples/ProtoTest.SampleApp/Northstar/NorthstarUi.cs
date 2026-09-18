namespace ProtoTest.SampleApp.Northstar;

using System.Net;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;

/// <summary>
/// A tiny server-rendered UI over the same store, so browser tests drive the real application: the
/// login form exchanges the tenant's bearer token for a cookie, and the projects page renders what the
/// API created.
/// </summary>
internal static class NorthstarUi
{
    private const string TokenCookie = "northstar_token";
    private const string Html = "text/html; charset=utf-8";

    public static void MapNorthstarUi(this WebApplication app)
    {
        app.MapGet("/login", (string? error) => Results.Content(LoginPage(error), Html));
        app.MapPost("/login", async (HttpContext http) =>
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var token = form["token"].ToString();
            try
            {
                _ = NorthstarHttp.Store(http).Authenticate(token);
            }
            catch (NorthstarException)
            {
                return Results.Content(
                    LoginPage("That token is not valid."),
                    Html,
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            http.Response.Cookies.Append(TokenCookie, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
            return Results.Redirect("/projects");
        });

        app.MapGet("/projects", (HttpContext http) =>
        {
            var token = http.Request.Cookies[TokenCookie];
            NorthstarPrincipal principal;
            try
            {
                principal = NorthstarHttp.Store(http).Authenticate(token);
            }
            catch (NorthstarException)
            {
                return Results.Redirect("/login");
            }

            var projects = NorthstarHttp.Store(http).ListProjects(principal, cursor: null, limit: 100).Items;
            return Results.Content(ProjectsPage(projects), Html);
        });
    }

    private static string LoginPage(string? error)
    {
        var notice = error is null
            ? string.Empty
            : $"<p data-testid=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</p>";
        return $$"""
            <!doctype html>
            <html lang="en"><head><title>Northstar · Sign in</title></head><body>
              <h1>Northstar</h1>
              <form method="post" action="/login">
                <label for="token">API token</label>
                <input id="token" name="token" data-testid="token" autocomplete="off">
                <button type="submit" data-testid="login">Sign in</button>
              </form>
              {{notice}}
            </body></html>
            """;
    }

    private static string ProjectsPage(IReadOnlyList<ProjectResponse> projects)
    {
        var rows = string.Concat(projects.Select(project => $"""
              <tr data-testid="project">
                <td data-testid="project-name">{WebUtility.HtmlEncode(project.Name)}</td>
                <td data-testid="project-status">{WebUtility.HtmlEncode(project.Status)}</td>
                <td data-testid="project-environments">{project.EnvironmentCount}</td>
              </tr>
            """));
        return $$"""
            <!doctype html>
            <html lang="en"><head><title>Northstar · Projects</title></head><body>
              <h1>Projects</h1>
              <label for="search">Search projects</label>
              <input id="search" data-testid="search" oninput="filter(this.value)">
              <table data-testid="projects">
                <thead><tr><th>Project</th><th>Status</th><th>Environments</th></tr></thead>
                <tbody>
            {{rows}}
                </tbody>
              </table>
              <script>
                function filter(text) {
                  for (const row of document.querySelectorAll('tbody tr')) {
                    row.hidden = !row.textContent.toLowerCase().includes(text.toLowerCase());
                  }
                }
              </script>
            </body></html>
            """;
    }
}
