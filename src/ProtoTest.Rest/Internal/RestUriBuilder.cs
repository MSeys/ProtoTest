using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProtoTest.Rest.Tests")]

namespace ProtoTest.Rest.Internal;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using ProtoTest.Http;

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
            if (!IsHttpUri(absoluteUri))
            {
                throw new InvalidOperationException(
                    $"REST requests require an HTTP or HTTPS URI, but '{absoluteUri.Scheme}' was supplied.");
            }

            return absoluteUri;
        }

        var baseAddress = baseAddressResolver is null
            ? configuredBaseAddress
            : await baseAddressResolver(context, cancellationToken);

        if (baseAddress is null)
        {
            throw new InvalidOperationException(
                "A relative REST request requires a configured or per-test base address.");
        }

        if (!baseAddress.IsAbsoluteUri || !IsHttpUri(baseAddress))
        {
            throw new InvalidOperationException(
                "A REST base address must be an absolute HTTP or HTTPS URI.");
        }

        return new Uri(baseAddress, target);
    }

    public static string BuildTarget(string template, object? routeAndQueryParams)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        if (routeAndQueryParams == null)
            return template;

        var properties = GetValues(routeAndQueryParams);

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Replace route tokens {id}
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

        return values.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => property.Name,
                property => property.GetValue(values),
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

    private static bool IsHttpUri(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
           || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
