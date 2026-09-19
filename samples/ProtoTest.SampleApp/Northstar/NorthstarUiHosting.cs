namespace ProtoTest.SampleApp.Northstar;

using System.Net;
using Microsoft.Extensions.FileProviders;

/// <summary>
/// Serves the built Vue console from <c>Northstar:Ui:Path</c> (default <c>Ui/dist</c>, relative to the
/// content root) under <c>/console</c>. The .NET build never invokes Node: when the folder is missing the
/// console routes answer with the command that produces it, and everything else keeps working untouched.
/// The server-rendered <c>/login</c> and <c>/projects</c> pages stay on the root, so the console takes a
/// namespace of its own instead of shadowing them.
/// </summary>
internal static class NorthstarUiHosting
{
    public const string DefaultPath = "Ui/dist";
    public const string RequestPath = "/console";

    public static void UseNorthstarConsole(this WebApplication app)
    {
        var configured = app.Configuration["Northstar:Ui:Path"];
        var path = string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured.Trim();
        var folder = System.IO.Path.GetFullPath(
            System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(app.Environment.ContentRootPath, path));

        app.MapGet("/", () => Results.Redirect(RequestPath + "/"));

        if (Directory.Exists(folder))
        {
            var provider = new PhysicalFileProvider(folder);
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = provider,
                RequestPath = RequestPath
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = provider,
                RequestPath = RequestPath
            });
            app.MapFallback(async context =>
            {
                if (IsConsolePath(context.Request.Path))
                {
                    await context.Response.SendFileAsync(System.IO.Path.Combine(folder, "index.html"));
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
            return;
        }

        // A folder without a build is an instruction, not an error: the API and the HTML pages are unaffected.
        app.MapFallback(async context =>
        {
            if (!IsConsolePath(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(NotBuiltPage(folder));
        });
    }

    private static bool IsConsolePath(PathString path)
        => path.StartsWithSegments(RequestPath, StringComparison.OrdinalIgnoreCase);

    private static string NotBuiltPage(string folder) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Northstar console · not built</title>
          <style>
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px;
                   background: #eef2f1; color: #16232c;
                   font: 15px/1.6 "Segoe UI", system-ui, -apple-system, sans-serif; }
            main { max-width: 620px; background: #fff; border: 1px solid #d9e2e0; border-radius: 12px; padding: 40px; }
            h1 { margin: 0 0 8px; font-size: 22px; letter-spacing: -0.01em; }
            p { margin: 0 0 16px; }
            code, pre { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: 14px; }
            code { background: #eef2f1; border: 1px solid #d9e2e0; border-radius: 6px; padding: 1px 6px; }
            pre { background: #16232c; color: #eef2f1; border-radius: 8px; padding: 14px 16px; overflow-x: auto; }
            ol { padding-left: 20px; margin: 0; }
            li { margin-bottom: 6px; }
            .where { color: #5f7180; font-size: 13px; margin-top: 18px; }
          </style>
        </head>
        <body>
          <main data-testid="ui-not-built">
            <h1>UI not built</h1>
            <p>The console has no build at <code>{{WebUtility.HtmlEncode(folder)}}</code>.</p>
            <ol>
              <li>Open a terminal in <code>samples/ProtoTest.SampleApp/Ui</code>.</li>
              <li><code>npm install</code></li>
              <li><code>npm run build</code></li>
            </ol>
            <p class="where">Set <code>Northstar:Ui:Path</code> to serve a build from somewhere else. The API under <code>/api/v1</code> and the pages under <code>/login</code> and <code>/projects</code> are unaffected.</p>
          </main>
        </body>
        </html>
        """;
}
