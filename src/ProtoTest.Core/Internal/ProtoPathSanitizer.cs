namespace ProtoTest.Core.Internal;

/// <summary>Shared sanitization for file names and archive path segments used in trace archives.</summary>
internal static class ProtoPathSanitizer
{
    /// <summary>
    /// Sanitizes an archive entry component. Archive paths must be readable on every platform, so the
    /// Windows-invalid set cannot be used: <see cref="Path.GetInvalidFileNameChars"/> differs per OS and
    /// would leave <c>:</c>, <c>?</c> or <c>\</c> in a name written on Linux. The same
    /// segment rule is used everywhere for that reason.
    /// </summary>
    public static string FileName(string value, string fallback) => Segment(value, fallback);

    /// <summary>Reduces a value to letters, digits, and '.', '-', '_' for use as a path segment.</summary>
    public static string Segment(string value, string fallback)
    {
        var sanitized = new string([.. value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_')]);
        return IsSafeSegment(sanitized) ? sanitized : fallback;
    }

    // An empty segment, or one made only of dots, is not a name: "." and ".." address the current and
    // parent directory, and trailing dots are stripped by the platform, so "..." collapses to "..".
    private static bool IsSafeSegment(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Any(character => character != '.');
}
