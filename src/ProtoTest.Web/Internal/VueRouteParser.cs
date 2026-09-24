namespace ProtoTest.Web.Internal;

using System.Text.Json;
using ProtoTest.Core.Internal;

/// <summary>
/// Turns the Vue Router discovery script's answer into normalized page routes. A malformed or empty
/// answer yields nothing; only absolute paths are application routes, because the script already
/// resolves Vue 2 child paths.
/// </summary>
internal static class VueRouteParser
{
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
