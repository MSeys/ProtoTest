namespace ProtoTest.Http;

/// <summary>Platform-independent HTTP URI classification shared by protocol integrations.</summary>
public static class ProtoHttpUri
{
    /// <summary>
    /// Returns whether the value starts with a URI scheme that may contain a colon, such as
    /// <c>https://</c> or <c>file:///</c>. A route segment that merely contains a colon, such as
    /// <c>orders:search</c>, is not a scheme: an explicit scheme always has a <c>://</c> separator.
    /// </summary>
    public static bool HasExplicitScheme(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0 || !char.IsAsciiLetter(value[0])) return false;
        return value.AsSpan(1, separator - 1).ToString().All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.');
    }

    public static bool TryCreateAbsoluteHttpUri(string value, out Uri? uri)
    {
        uri = null;
        if (!HasExplicitScheme(value) || !Uri.TryCreate(value, UriKind.Absolute, out var created)) return false;
        uri = created;
        return IsHttpUri(created);
    }

    /// <summary>Returns whether <paramref name="uri"/> uses the HTTP or HTTPS scheme.</summary>
    public static bool IsHttpUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
