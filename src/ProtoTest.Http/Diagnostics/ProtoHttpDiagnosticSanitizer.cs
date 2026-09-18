namespace ProtoTest.Http;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Redacts sensitive values from HTTP request/response diagnostics before they are traced or attached.
/// Shared by the REST and GraphQL integrations.
/// </summary>
public static class ProtoHttpDiagnosticSanitizer
{
    internal const string RedactedValue = ProtoUriSanitizer.RedactedValue;

    /// <summary>Sanitizes a body using the configured JSON redaction rules.</summary>
    public static string SanitizeBody(string content, ProtoHttpAttachmentOptions? configuredOptions)
        => JsonDiagnosticSanitizer.Sanitize(content, configuredOptions);

    /// <summary>Returns the supplied headers with sensitive values replaced.</summary>
    public static IReadOnlyDictionary<string, string> SanitizeHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        ProtoHttpAttachmentOptions? configuredOptions)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var options = configuredOptions ?? new ProtoHttpAttachmentOptions();
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

    /// <summary>Returns the URI with credentials and sensitive query parameter values removed.</summary>
    public static string? SanitizeUri(Uri? uri, ProtoHttpAttachmentOptions? configuredOptions)
    {
        if (uri is null)
        {
            return null;
        }

        var options = configuredOptions ?? new ProtoHttpAttachmentOptions();
        // User-info is always removed: credentials are never traced, whatever the redaction toggle says.
        return options.RedactSensitiveData
            ? ProtoUriSanitizer.Sanitize(uri, options.SensitiveQueryParameters)
            : ProtoUriSanitizer.WithoutUserInfo(uri.OriginalString);
    }
}
