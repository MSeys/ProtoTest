namespace ProtoTest.Rest.Internal;

using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RestDiagnosticSanitizer
{
    private const string RedactedValue = "[REDACTED]";

    public static string SanitizeBody(
        string content,
        string? mediaType,
        RestAttachmentOptions? configuredOptions)
    {
        var options = configuredOptions ?? new RestAttachmentOptions();
        var sanitized = content ?? string.Empty;

        if (options.RedactSensitiveData
            && (IsJson(mediaType) || LooksLikeJson(sanitized))
            && sanitized.Length > 0)
        {
            sanitized = RedactJson(sanitized, options.SensitiveJsonProperties);
        }

        var limit = Math.Max(0, options.MaxDiagnosticBodyLength);
        if (sanitized.Length > limit)
        {
            var omitted = sanitized.Length - limit;
            sanitized = $"{sanitized[..limit]}{Environment.NewLine}… [{omitted} characters truncated]";
        }

        return sanitized;
    }

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

    private static bool IsJson(string? mediaType)
        => mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    private static bool LooksLikeJson(string content)
    {
        var trimmed = content.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private static string RedactJson(string content, IEnumerable<string> propertyNames)
    {
        try
        {
            var node = JsonNode.Parse(content);
            if (node is null)
            {
                return content;
            }

            var sensitive = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
            return RedactNode(node, sensitive)
                ? node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
                : content;
        }
        catch (JsonException)
        {
            return content;
        }
    }

    private static bool RedactNode(JsonNode node, HashSet<string> sensitive)
    {
        var changed = false;
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (sensitive.Contains(property.Key))
                {
                    jsonObject[property.Key] = RedactedValue;
                    changed = true;
                }
                else if (property.Value is not null)
                {
                    changed |= RedactNode(property.Value, sensitive);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    changed |= RedactNode(item, sensitive);
                }
            }
        }

        return changed;
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
