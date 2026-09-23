namespace ProtoTest.Sheets.Internal;

/// <summary>
/// The value phrasing the sheet assertion facades share, so a count and a text value read the same
/// whichever assertion reported them.
/// </summary>
internal static class SheetAssertionText
{
    /// <summary>Counts values as "1 value" or "3 values".</summary>
    public static string Count(int count) => $"{count} {(count == 1 ? "value" : "values")}";

    /// <summary>Formats a text value: null is empty, anything else is quoted.</summary>
    public static string Format(string? value) => value is null ? "empty" : $"'{value}'";
}
