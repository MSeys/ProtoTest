namespace ProtoTest.Http.Internal;

internal static class ProtoQueryString
{
    public static string SetParameter(string url, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var (withoutFragment, fragment) = SplitFragment(url);
        var queryIndex = withoutFragment.IndexOf('?');
        var path = queryIndex < 0 ? withoutFragment : withoutFragment[..queryIndex];
        var existingQuery = queryIndex < 0 ? string.Empty : withoutFragment[(queryIndex + 1)..];
        var encodedKey = Uri.EscapeDataString(key);
        var parameters = existingQuery
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !HasQueryKey(parameter, key))
            .Append($"{encodedKey}={Uri.EscapeDataString(value)}");

        return $"{path}?{string.Join('&', parameters)}{fragment}";
    }

    private static (string WithoutFragment, string Fragment) SplitFragment(string url)
    {
        var fragmentIndex = url.IndexOf('#');
        return fragmentIndex < 0
            ? (url, string.Empty)
            : (url[..fragmentIndex], url[fragmentIndex..]);
    }

    private static bool HasQueryKey(string parameter, string expectedKey)
    {
        var separatorIndex = parameter.IndexOf('=');
        var encodedKey = separatorIndex < 0 ? parameter : parameter[..separatorIndex];
        var decodedKey = Uri.UnescapeDataString(encodedKey.Replace("+", " ", StringComparison.Ordinal));
        return string.Equals(decodedKey, expectedKey, StringComparison.OrdinalIgnoreCase);
    }
}
