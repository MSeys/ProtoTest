namespace ProtoTest.Rest.Internal;

using ProtoTest.Json;

internal static class RestDiagnosticSanitizer
{
    private const string RedactedValue = "[REDACTED]";

    public static string SanitizeBody(
        string content,
        string? mediaType,
        RestAttachmentOptions? configuredOptions)
        => JsonDiagnosticSanitizer.Sanitize(content, configuredOptions);

    public static IReadOnlyDictionary<string, string> SanitizeHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        RestAttachmentOptions? configuredOptions)
    {
        var options = configuredOptions ?? new RestAttachmentOptions();
        var sensitive = new HashSet<string>(options.SensitiveHeaders, StringComparer.OrdinalIgnoreCase);

        return headers
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
            group => group.Key,
            group => options.RedactSensitiveData && sensitive.Contains(group.Key)
                ? RedactedValue
                : string.Join(", ", group.SelectMany(header => header.Value)),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string? SanitizeUri(Uri? uri, RestAttachmentOptions? configuredOptions)
    {
        if (uri is null)
        {
            return null;
        }

        var options = configuredOptions ?? new RestAttachmentOptions();
        if (!options.RedactSensitiveData)
        {
            return uri.ToString();
        }

        var original = uri.OriginalString;
        var fragmentIndex = original.IndexOf('#');
        var fragment = fragmentIndex < 0 ? string.Empty : original[fragmentIndex..];
        var withoutFragment = fragmentIndex < 0 ? original : original[..fragmentIndex];
        var queryIndex = withoutFragment.IndexOf('?');
        if (queryIndex < 0)
        {
            return original;
        }

        var sensitive = new HashSet<string>(options.SensitiveQueryParameters, StringComparer.OrdinalIgnoreCase);
        var path = withoutFragment[..queryIndex];
        var parameters = withoutFragment[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => RedactQueryParameter(parameter, sensitive));
        return $"{path}?{string.Join('&', parameters)}{fragment}";
    }

    private static string RedactQueryParameter(string parameter, HashSet<string> sensitive)
    {
        var separatorIndex = parameter.IndexOf('=');
        var encodedKey = separatorIndex < 0 ? parameter : parameter[..separatorIndex];
        var key = Uri.UnescapeDataString(encodedKey.Replace("+", " ", StringComparison.Ordinal));
        return sensitive.Contains(key)
            ? $"{encodedKey}={Uri.EscapeDataString(RedactedValue)}"
            : parameter;
    }
}
