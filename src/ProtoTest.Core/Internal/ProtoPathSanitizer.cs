namespace ProtoTest.Core.Internal;

/// <summary>Shared sanitization for file names and archive path segments used in trace archives.</summary>
internal static class ProtoPathSanitizer
{
    /// <summary>Replaces filesystem-invalid characters with '_'.</summary>
    public static string FileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. value.Select(character => invalid.Contains(character) ? '_' : character)]);
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    /// <summary>Reduces a value to letters, digits, and '.', '-', '_' for use as a path segment.</summary>
    public static string Segment(string value, string fallback)
    {
        var sanitized = new string([.. value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_')]);
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
