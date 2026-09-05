using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProtoTest.Rest.Tests")]

namespace ProtoTest.Rest.Internal;

using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

internal static partial class RouteAndQueryParser
{
    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex RouteTokenRegex();

    public static string BuildUrl(string template, object? routeAndQueryParams)
    {
        if (routeAndQueryParams == null)
            return template;

        var properties = routeAndQueryParams.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.GetValue(routeAndQueryParams), StringComparer.OrdinalIgnoreCase);

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Replace route tokens {id}
        var resolvedUrl = RouteTokenRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (properties.TryGetValue(key, out var val) && val != null)
            {
                usedKeys.Add(key);
                return HttpUtility.UrlEncode(val.ToString())!;
            }
            return match.Value;
        });

        // 2. Append unused properties as query parameters
        var queryParams = properties.Where(kvp => !usedKeys.Contains(kvp.Key) && kvp.Value != null).ToList();

        if (queryParams.Count == 0)
            return resolvedUrl;

        var sb = new StringBuilder(resolvedUrl);
        sb.Append(resolvedUrl.Contains('?') ? '&' : '?');

        for (int i = 0; i < queryParams.Count; i++)
        {
            if (i > 0) sb.Append('&');

            var (key, value) = queryParams[i];

            if (value is not string && value is IEnumerable collection)
            {
                var first = true;
                foreach (var item in collection)
                {
                    if (item == null) continue;
                    if (!first) sb.Append('&');
                    sb.Append(HttpUtility.UrlEncode(key)).Append('=').Append(HttpUtility.UrlEncode(item.ToString()));
                    first = false;
                }
            }
            else
            {
                sb.Append(HttpUtility.UrlEncode(key)).Append('=').Append(HttpUtility.UrlEncode(value!.ToString()));
            }
        }

        return sb.ToString();
    }
}