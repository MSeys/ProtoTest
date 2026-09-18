namespace ProtoTest.Core;

/// <summary>
/// Text-level redaction for the addresses integrations trace: URI user-info (credentials) is removed
/// unconditionally, and the values of well-known sensitive query parameters are replaced. Shared by
/// HTTP, gRPC, messaging and web so one policy cannot drift between packages.
/// </summary>
public static class ProtoUriSanitizer
{
    /// <summary>The value sensitive parameters are replaced with.</summary>
    public const string RedactedValue = "[REDACTED]";

    /// <summary>The query parameter names redacted when an integration has no rule of its own.</summary>
    public static IReadOnlyList<string> DefaultSensitiveQueryParameters { get; } =
    [
        "access_token",
        "refresh_token",
        "token",
        "apiKey",
        "api_key",
        "key"
    ];

    /// <summary>Removes <c>user:password@</c> from an address, leaving everything else untouched.</summary>
    public static string WithoutUserInfo(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var schemeIndex = address.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex < 0)
        {
            return address;
        }

        var authorityStart = schemeIndex + 3;
        var authorityEnd = address.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = address.Length;
        }

        var at = address.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
        return at < authorityStart ? address : address.Remove(authorityStart, at - authorityStart + 1);
    }

    /// <summary>Sanitizes an address with the supplied sensitive query parameter names.</summary>
    public static string Sanitize(string address, IReadOnlyCollection<string>? sensitiveQueryParameters = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        var safe = WithoutUserInfo(address);
        if (sensitiveQueryParameters is null || sensitiveQueryParameters.Count == 0)
        {
            return safe;
        }

        var fragmentIndex = safe.IndexOf('#');
        var fragment = fragmentIndex < 0 ? string.Empty : safe[fragmentIndex..];
        var withoutFragment = fragmentIndex < 0 ? safe : safe[..fragmentIndex];
        var queryIndex = withoutFragment.IndexOf('?');
        if (queryIndex < 0)
        {
            return safe;
        }

        var sensitive = new HashSet<string>(sensitiveQueryParameters, StringComparer.OrdinalIgnoreCase);
        var path = withoutFragment[..queryIndex];
        var parameters = withoutFragment[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => RedactQueryParameter(parameter, sensitive));
        return $"{path}?{string.Join('&', parameters)}{fragment}";
    }

    /// <summary>Sanitizes an address with the default sensitive query parameter names.</summary>
    public static string? Sanitize(Uri? address, IReadOnlyCollection<string>? sensitiveQueryParameters = null)
        => address is null
            ? null
            : Sanitize(address.OriginalString, sensitiveQueryParameters ?? DefaultSensitiveQueryParameters);

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
