namespace ProtoTest.Web.Internal;

using System.Text.Json;

/// <summary>
/// Best-effort Vue Router route discovery for the page inventory. The script answers with a JSON array
/// of route paths, or <see langword="null"/> when there is no Vue application or router — a missing
/// framework is an answer, not a failure — and malformed answers are ignored.
/// </summary>
internal static class VueRouteDiscovery
{
    internal const string Script = """
        (() => {
          try {
            const walk = (routes, parent, paths) => {
              if (!Array.isArray(routes)) return;
              for (const route of routes) {
                if (!route || typeof route.path !== 'string') continue;
                if (!route.path) {
                  // An empty path is the parent's default route: it adds no segment, but its children
                  // still resolve against the parent path.
                  if (route.children) walk(route.children, parent, paths);
                  continue;
                }
                let path = route.path;
                if (path.charAt(0) !== '/') {
                  // Vue 2 child paths are relative to their parent. A top-level relative path is not
                  // resolvable against an application route, so it is not a page.
                  if (!parent) continue;
                  path = parent.replace(/\/+$/, '') + '/' + path.replace(/^\/+/, '');
                }
                paths.push(path);
                if (route.children) walk(route.children, path, paths);
              }
            };
            const app = document.querySelector('[data-v-app]');
            const vueApp = app && app.__vue_app__ ? app.__vue_app__ : null;
            const properties = vueApp && vueApp.config ? vueApp.config.globalProperties : null;
            const router = properties ? properties.$router : null;
            let routes = router && typeof router.getRoutes === 'function' ? router.getRoutes() : null;
            if (!routes) {
              const root = document.querySelector('#app');
              routes = root && root.__vue__ && root.__vue__.$router && root.__vue__.$router.options
                ? root.__vue__.$router.options.routes
                : null;
            }
            if (!routes) return null;
            const paths = [];
            walk(routes, null, paths);
            return JSON.stringify([...new Set(paths)]);
          } catch (error) {
            return null;
          }
        })()
        """;

    public static IEnumerable<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        string[]? paths;
        try
        {
            paths = JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        if (paths is null) yield break;
        foreach (var path in paths)
        {
            // Only absolute paths are application routes; the page script resolves Vue 2 children, so a
            // relative value here means it was not a route.
            if (string.IsNullOrWhiteSpace(path) || !path.TrimStart().StartsWith('/')) continue;
            var normalized = WebPagePath.NormalizeRoute(path);
            if (normalized is not null) yield return normalized;
        }
    }
}
