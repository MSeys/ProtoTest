namespace ProtoTest.Web;

/// <summary>Shared helpers for producing safe, lowercase identifiers for web artifacts.</summary>
public static class WebNames
{
    /// <summary>Reduces a value to lowercase letters, digits, and single dashes for use in file names.</summary>
    public static string SafeName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '-'))
            .Trim('-')
            .ToLowerInvariant();
    }
}
