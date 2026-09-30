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

    /// <summary>
    /// The query parameter names redacted when an integration has no rule of its own. The shared
    /// redaction defaults, plus <c>key</c>, so traced URLs redact what state values redact.
    /// </summary>
    public static IReadOnlyList<string> DefaultSensitiveQueryParameters { get; } =
        [.. ProtoRedactionDefaults.SensitivePropertyNames, "key"];

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

    /// <summary>
    /// The display form of an address for a trace field or an artifact: user info removed, everything
    /// else - including the query and fragment - kept. A value that is not an absolute address is
    /// returned as it is, so a location is never dropped.
    /// </summary>
    public static string? ForDisplay(string? address)
        => address is null ? null : Sanitize(address, sensitiveQueryParameters: null);

    /// <summary>
    /// The diagnostic form of a request address: user info removed and the query and fragment dropped,
    /// so a logged request URL cannot leak a token through a query parameter the policy does not know.
    /// </summary>
    public static string? ForDiagnostics(string? address)
    {
        if (address is null)
        {
            return null;
        }

        var withoutUserInfo = WithoutUserInfo(address);
        return Uri.TryCreate(withoutUserInfo, UriKind.Absolute, out var uri)
            ? new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString()
            : withoutUserInfo;
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
