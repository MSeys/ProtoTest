namespace ProtoTest.Rest.Internal;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoTest.Http;
using ProtoTest.Json;

internal static partial class RestUriBuilder
{
    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex RouteTokenRegex();

    public static async ValueTask<Uri> BuildRequestUriAsync(
        string template,
        object? routeAndQueryParams,
        Uri? configuredBaseAddress,
        Func<ProtoTest.Core.ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? baseAddressResolver,
        ProtoTest.Core.ProtoExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = BuildTarget(template, routeAndQueryParams);
        // On Unix, System.Uri interprets a root-relative path such as "/orders" as an
        // absolute file URI. A REST route is absolute only when the caller supplied an
        // explicit URI scheme; root-relative routes must still resolve against BaseAddress.
        if (ProtoHttpUri.HasExplicitScheme(target))
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
                throw new InvalidOperationException($"REST request URI '{target}' is not a valid absolute URI.");
            // The shared rule: an absolute HTTP or HTTPS address, validated by ProtoHttpEndpoint the
            // same way GraphQL's per-test endpoint is.
            return ProtoHttpEndpoint.RequireHttpAddress(absoluteUri, $"REST request URI '{target}'");
        }

        var baseAddress = baseAddressResolver is null
            ? configuredBaseAddress
            : await baseAddressResolver(context, cancellationToken);

        if (baseAddress is null)
        {
            throw new InvalidOperationException(
                "A relative REST request requires a configured or per-test base address.");
        }

        return new Uri(
            ProtoHttpEndpoint.RequireHttpAddress(baseAddress, "A REST base address"),
            MakeRelativeTarget(target));
    }

    /// <summary>
    /// The route target relative to a base address, with a colon-containing first segment kept
    /// relative as RFC 3986 requires: Uri would otherwise read "orders:search" as a scheme.
    /// Exposed for testing.
    /// </summary>
    internal static string MakeRelativeTarget(string target)
    {
        var pathEnd = target.IndexOfAny(['?', '#']);
        var path = pathEnd < 0 ? target : target[..pathEnd];
        return !path.StartsWith('/') && path.Contains(':') ? $"./{target}" : target;
    }

    public static string BuildTarget(string template, object? routeAndQueryParams)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        if (routeAndQueryParams == null)
            return template;

        var properties = GetValues(routeAndQueryParams);

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Replace route tokens {id} first; every value left over becomes a query parameter.
        var resolvedUrl = RouteTokenRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (properties.TryGetValue(key, out var value) && value is not null)
            {
                usedKeys.Add(key);
                return Uri.EscapeDataString(FormatValue(value));
            }

            throw new ArgumentException(
                $"No non-null value was supplied for route parameter '{{{key}}}'.",
                nameof(routeAndQueryParams));
        });

        var queryParams = properties
            .Where(pair => !usedKeys.Contains(pair.Key) && pair.Value is not null)
            .SelectMany(pair => ExpandQueryValue(pair.Key, pair.Value!))
            .ToArray();

        if (queryParams.Length == 0)
            return resolvedUrl;

        return AppendQueryParameters(resolvedUrl, queryParams);
    }

    private static IReadOnlyDictionary<string, object?> GetValues(object values)
    {
        if (values is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (values is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new ArgumentException("Route and query dictionaries must use string keys.", nameof(values));
                }

                result[key] = entry.Value;
            }

            return result;
        }

        return ProtoJsonPropertyProjection.Read(values, JsonNamingPolicy.CamelCase)
            .ToDictionary(
                property => property.Name,
                property => property.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<KeyValuePair<string, string>> ExpandQueryValue(string key, object value)
    {
        if (value is string || value is not IEnumerable values)
        {
            yield return new KeyValuePair<string, string>(key, FormatValue(value));
            yield break;
        }

        foreach (var item in values)
        {
            if (item is not null)
            {
                yield return new KeyValuePair<string, string>(key, FormatValue(item));
            }
        }
    }

    private static string AppendQueryParameters(
        string url,
        IReadOnlyCollection<KeyValuePair<string, string>> parameters)
    {
        var (withoutFragment, fragment) = SplitFragment(url);
        var separator = withoutFragment.Contains('?') ? '&' : '?';
        var query = string.Join('&', parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return $"{withoutFragment}{separator}{query}{fragment}";
    }

    private static (string WithoutFragment, string Fragment) SplitFragment(string url)
    {
        var fragmentIndex = url.IndexOf('#');
        return fragmentIndex < 0
            ? (url, string.Empty)
            : (url[..fragmentIndex], url[fragmentIndex..]);
    }

    private static string FormatValue(object value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        DateOnly dateOnly => dateOnly.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly timeOnly => timeOnly.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };
}
