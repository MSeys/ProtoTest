namespace ProtoTest.Web.Internal;

using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Best-effort page inventory from a frontend source folder, so pages that exist in a
/// Vue/React/Next/Nuxt/Remix project show as uncovered until a browser test verifies them. Discovery is
/// silently empty for a missing or unreadable folder: a framework that isn't there is an answer, not a
/// failure. The scanner applies one strategy family:
/// <list type="bullet">
/// <item>File-based routers: Next.js/Nuxt <c>pages/</c> and <c>src/pages/</c>, the Next.js <c>app/</c>
/// router, and Remix <c>app/routes/</c> flat file names.</item>
/// <item>Literal route scan: absolute <c>path:</c>/<c>path =</c> literals and JSX
/// <c>&lt;Route path="/…"&gt;</c> usages in Vue Router and React Router sources — the fallback when
/// <c>auto</c> detects nothing file-based.</item>
/// </list>
/// Only absolute literals are collected; relative child routes and aliased imports are not resolved.
/// Dynamic segments normalize to <c>{name}</c>; star splats, bare Remix splat names and bracketed
/// catch-alls (<c>[...slug]</c>, <c>[[...slug]]</c>) normalize to <c>{...}</c>, matching
/// <see cref="WebPagePath.Matches"/>. Nuxt 2 underscore-prefixed dynamic files (<c>_id.vue</c>,
/// <c>_.vue</c>) are not mapped. Next API handlers (<c>pages/api/…</c>), test/spec files and
/// <c>.d.ts</c> declarations never become pages. A relative source folder is combined with the test
/// assembly's base directory and may not escape it; an absolute source folder is used as given. The
/// package.json walk is bounded to
/// <see cref="MaxPackageJsonLevels"/> folders, and the file walk to <see cref="MaxSourceFiles"/>.
/// </summary>
internal static class WebPageSourceScanner
{
    private static readonly string[] RouteExtensions = [".ts", ".tsx", ".js", ".jsx", ".vue"];
    private static readonly string[] SkippedDirectories = ["node_modules", "dist", "build", ".next", "coverage"];
    private static readonly string[] NextSpecialPages = ["_app", "_document", "_error", "404", "500", "_middleware"];

    /// <summary>How far above the resolved source folder a directory may sit before its package.json is authoritative.</summary>
    private const int MaxPackageJsonLevels = 3;

    /// <summary>The most route candidate files one discovery walks; a huge tree stops instead of crawling forever.</summary>
    private const int MaxSourceFiles = 10_000;

    /// <summary>Files larger than this are not scanned for route literals.</summary>
    private const long MaxSourceFileBytes = 1_000_000;

    private static readonly Regex RouteLiteral = new(
        """(?<![A-Za-z0-9_$])path\s*[:=]\s*["'](?<value>[^"']+)["']""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Discovers normalized route patterns from <paramref name="sourceFolder"/>, absolute or relative to
    /// <see cref="AppContext.BaseDirectory"/>. <paramref name="framework"/> is <c>auto</c> (default) or one
    /// of <c>next</c>, <c>nuxt</c>, <c>remix</c>, <c>vue</c>, <c>react</c>; an unknown value falls back to
    /// <c>auto</c>.
    /// </summary>
    public static IReadOnlyList<string> Discover(string? sourceFolder, string? framework)
    {
        var folder = ResolveFolder(sourceFolder);
        if (folder is null) return [];

        try
        {
            if (!Directory.Exists(folder)) return [];
            var kind = ParseFramework(framework);
            if (kind == Framework.Auto) kind = Detect(folder);
            var routes = new SortedSet<string>(StringComparer.Ordinal);
            switch (kind)
            {
                case Framework.Next:
                    ScanPagesDirectory(folder, routes, skipApiSegment: true);
                    ScanAppRouter(folder, routes);
                    break;
                case Framework.Nuxt:
                    ScanPagesDirectory(folder, routes, skipApiSegment: false);
                    break;
                case Framework.Remix:
                    ScanRemixRoutes(folder, routes);
                    break;
                default:
                    ScanRouteLiterals(folder, routes);
                    break;
            }

            return [.. routes];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Resolves a configured source folder to a canonical absolute path, or <see langword="null"/> when
    /// the value is empty, malformed, or a relative path that escapes the test assembly's base directory.
    /// The resolved folder is recorded once and used as the root of the whole discovery: the package.json
    /// walk is bounded relative to it and no directory above it is enumerated.
    /// </summary>
    internal static string? ResolveFolder(string? sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder)) return null;
        try
        {
            var value = sourceFolder.Trim();
            var baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var resolved = Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(baseDirectory, value));
            // A relative folder must stay inside the base directory; an absolute folder is used as given.
            if (!Path.IsPathRooted(value) && !IsInside(resolved, baseDirectory)) return null;
            return resolved;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsInside(string path, string baseDirectory)
        => path.Equals(baseDirectory, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(baseDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(baseDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal enum Framework
    {
        Auto,
        Next,
        Nuxt,
        Remix,
        Vue,
        React
    }

    private static Framework ParseFramework(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "next" => Framework.Next,
            "nuxt" => Framework.Nuxt,
            "remix" => Framework.Remix,
            "vue" => Framework.Vue,
            "react" => Framework.React,
            _ => Framework.Auto
        };

    /// <summary>Detects the framework from the nearest <c>package.json</c>, then from the folder layout.</summary>
    private static Framework Detect(string folder)
    {
        var fromPackage = DetectFromPackageJson(folder);
        return fromPackage != Framework.Auto ? fromPackage : DetectFromLayout(folder);
    }

    /// <summary>
    /// Reads the nearest <c>package.json</c>, walking at most <see cref="MaxPackageJsonLevels"/> directories
    /// up from the resolved folder. The first package file found is the project boundary: the walk stops
    /// there, so a parent repository's dependencies never decide how this folder is scanned.
    /// </summary>
    private static Framework DetectFromPackageJson(string folder)
    {
        var current = new DirectoryInfo(folder);
        for (var level = 0; current is not null && level <= MaxPackageJsonLevels; level++, current = current.Parent)
        {
            var package = Path.Combine(current.FullName, "package.json");
            if (File.Exists(package)) return ReadPackageFramework(package);
        }

        return Framework.Auto;
    }

    private static Framework ReadPackageFramework(string package)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(package));
            var root = document.RootElement;
            if (HasDependency(root, "next")) return Framework.Next;
            if (HasDependency(root, "nuxt")) return Framework.Nuxt;
            if (HasDependency(root, "@remix-run/react")
                || HasDependency(root, "@remix-run/node")
                || HasDependency(root, "@remix-run/dev"))
            {
                return Framework.Remix;
            }

            if (HasDependency(root, "vue")) return Framework.Vue;
            if (HasDependency(root, "react")) return Framework.React;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return Framework.Auto;
    }

    private static bool HasDependency(JsonElement root, string name)
        => (root.TryGetProperty("dependencies", out var dependencies) && dependencies.TryGetProperty(name, out _))
           || (root.TryGetProperty("devDependencies", out var devDependencies) && devDependencies.TryGetProperty(name, out _));

    internal static Framework DetectFromLayout(string folder)
    {
        if (HasFile(folder, "next.config.*")) return Framework.Next;
        if (HasFile(folder, "nuxt.config.*")) return Framework.Nuxt;
        if (Directory.Exists(Path.Combine(folder, "app", "routes")))
        {
            // A Next.js app-router project can own an app/routes folder; page.* files anywhere under
            // app/ mean Next, not Remix.
            return HasFile(Path.Combine(folder, "app"), "page.*", SearchOption.AllDirectories)
                   || HasFile(Path.Combine(folder, "src", "app"), "page.*", SearchOption.AllDirectories)
                ? Framework.Next
                : Framework.Remix;
        }

        if (HasFile(Path.Combine(folder, "app"), "page.*")
            || HasFile(Path.Combine(folder, "src", "app"), "page.*"))
        {
            return Framework.Next;
        }

        foreach (var pages in new[] { Path.Combine(folder, "pages"), Path.Combine(folder, "src", "pages") })
        {
            if (Directory.Exists(pages)) return HasExtension(pages, ".vue", SearchOption.AllDirectories)
                ? Framework.Nuxt
                : Framework.Next;
        }

        return Framework.Auto;
    }

    /// <summary>
    /// Next.js/Nuxt file routes: <c>pages/</c> and <c>src/pages/</c>, extensions stripped, nested
    /// folders as segments, <c>index</c> as the folder's route, dynamics as <c>{name}</c>. Next's
    /// <c>pages/api</c> handlers are not pages, so an <c>api</c> path segment is skipped there.
    /// </summary>
    private static void ScanPagesDirectory(string folder, SortedSet<string> routes, bool skipApiSegment)
    {
        foreach (var rootName in new[] { "pages", Path.Combine("src", "pages") })
        {
            var root = Path.Combine(folder, rootName);
            if (!Directory.Exists(root)) continue;
            foreach (var file in EnumerateSourceFiles(root))
            {
                var relative = Path.GetRelativePath(root, file);
                if (skipApiSegment && HasApiSegment(relative)) continue;
                var name = Path.GetFileNameWithoutExtension(relative);
                if (NextSpecialPages.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var segments = SplitSegments(Path.GetDirectoryName(relative));
                if (!name.Equals("index", StringComparison.OrdinalIgnoreCase)) segments.Add(name);
                routes.Add(BuildRoute(segments));
            }
        }
    }

    private static bool HasApiSegment(string relativePath)
        => SplitSegments(Path.GetDirectoryName(relativePath))
            .Any(segment => segment.Equals("api", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Next.js app router: <c>page.*</c> under <c>app/</c> or <c>src/app/</c>. Route groups
    /// <c>(group)</c> drop out of the path; <c>layout</c>, <c>template</c>, <c>loading</c>, <c>error</c>
    /// and <c>not-found</c> files never produce a route because only <c>page.*</c> does.
    /// </summary>
    private static void ScanAppRouter(string folder, SortedSet<string> routes)
    {
        foreach (var rootName in new[] { "app", Path.Combine("src", "app") })
        {
            var root = Path.Combine(folder, rootName);
            if (!Directory.Exists(root)) continue;
            foreach (var file in EnumerateSourceFiles(root))
            {
                if (!Path.GetFileNameWithoutExtension(file).Equals("page", StringComparison.OrdinalIgnoreCase)) continue;
                var segments = SplitSegments(Path.GetDirectoryName(Path.GetRelativePath(root, file)))
                    .Where(segment => !(segment.StartsWith('(') && segment.EndsWith(')')));
                routes.Add(BuildRoute(segments));
            }
        }
    }

    /// <summary>
    /// Remix flat routes: <c>app/routes/</c> file names where dots become path segments,
    /// <c>_index</c> is the parent's route, leading <c>_</c> segments are pathless and <c>$id</c> is a
    /// dynamic segment.
    /// </summary>
    private static void ScanRemixRoutes(string folder, SortedSet<string> routes)
    {
        var root = Path.Combine(folder, "app", "routes");
        if (!Directory.Exists(root)) return;
        foreach (var file in EnumerateSourceFiles(root))
        {
            var segments = Path.GetFileNameWithoutExtension(file)
                .Split('.')
                .Where(segment => !segment.StartsWith('_'));
            routes.Add(BuildRoute(segments));
        }
    }

    /// <summary>
    /// Vue Router / React Router literals: <c>path: "…"</c>, <c>path: '…'</c>, <c>path = "…"</c> and
    /// JSX <c>&lt;Route path="/…"&gt;</c>, absolute values only.
    /// </summary>
    private static void ScanRouteLiterals(string folder, SortedSet<string> routes)
    {
        foreach (var file in EnumerateSourceFiles(folder))
        {
            string text;
            try
            {
                if (new FileInfo(file).Length > MaxSourceFileBytes) continue;
                text = File.ReadAllText(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in RouteLiteral.Matches(text))
            {
                var path = NormalizeLiteral(match.Groups["value"].Value);
                if (path is not null) routes.Add(path);
            }
        }
    }

    private static string? NormalizeLiteral(string value)
    {
        value = value.Trim();
        if (!value.StartsWith('/')) return null;
        return WebPagePath.NormalizeRoute(value);
    }

    /// <summary>
    /// Source files with a route extension, depth-first, skipping build and dependency folders. Directories
    /// that are reparse points (symlinks and junctions) are never followed, every directory is visited
    /// at most once by its normalized full path, so a self-referencing tree cannot loop or blow the stack,
    /// and the walk stops at <see cref="MaxSourceFiles"/>. Test/spec files and TypeScript declaration
    /// files are not routes.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string folder)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(folder);
        visited.Add(NormalizeDirectory(folder));
        var count = 0;
        while (pending.Count > 0)
        {
            if (count >= MaxSourceFiles) yield break;
            var current = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                if (IsReparsePoint(current)) continue;
                files = Directory.GetFiles(current);
                directories = Directory.GetDirectories(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!IsRouteCandidate(file)) continue;
                if (count++ >= MaxSourceFiles) yield break;
                yield return file;
            }

            foreach (var directory in directories)
            {
                if (SkippedDirectories.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)) continue;
                if (!visited.Add(NormalizeDirectory(directory))) continue;
                pending.Push(directory);
            }
        }
    }

    /// <summary>
    /// A route candidate has a route extension but is neither a test/spec file (<c>users.test.tsx</c>,
    /// <c>users.spec.ts</c>) nor a TypeScript declaration (<c>types.d.ts</c>), none of which are pages.
    /// </summary>
    private static bool IsRouteCandidate(string file)
    {
        if (!RouteExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) return false;
        var fileName = Path.GetFileName(file);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (Path.GetExtension(fileName).Equals(".ts", StringComparison.OrdinalIgnoreCase)
            && stem.EndsWith(".d", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !stem.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
               && !stem.EndsWith(".spec", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string directory)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return directory;
        }
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<string> SplitSegments(string? directory)
        => string.IsNullOrEmpty(directory) || directory == "."
            ? []
            : [.. directory.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)];

    private static string BuildRoute(IEnumerable<string> segments)
    {
        var mapped = segments.Select(WebPagePath.MapDynamicSegment).Where(segment => segment.Length > 0).ToList();
        return mapped.Count == 0 ? "/" : "/" + string.Join('/', mapped);
    }

    private static bool HasFile(string folder, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, pattern, option).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Exact extension match. A Windows wildcard search for <c>*.vue</c> also matches <c>.vuex</c>, which
    /// would misdetect Nuxt; comparing the extension avoids that.
    /// </summary>
    private static bool HasExtension(string folder, string extension, SearchOption option)
    {
        try
        {
            if (!Directory.Exists(folder)) return false;
            var examined = 0;
            foreach (var file in Directory.EnumerateFiles(folder, "*", option))
            {
                if (Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase)) return true;
                if (++examined >= MaxSourceFiles) break;
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
