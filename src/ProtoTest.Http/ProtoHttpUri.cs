namespace ProtoTest.Http;

/// <summary>Platform-independent HTTP URI classification shared by protocol integrations.</summary>
public static class ProtoHttpUri
{
    public static bool HasExplicitScheme(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var separator = value.IndexOf(':');
        if (separator <= 0 || !char.IsAsciiLetter(value[0])) return false;
        return value.AsSpan(1, separator - 1).ToString().All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.');
    }

    public static bool TryCreateAbsoluteHttpUri(string value, out Uri? uri)
    {
        uri = null;
        if (!HasExplicitScheme(value) || !Uri.TryCreate(value, UriKind.Absolute, out var created)) return false;
        uri = created;
        return created.Scheme is "http" or "https";
    }
}
