namespace ProtoTest.Reporting;

using System.Globalization;

/// <summary>
/// The report's one plural source, so every count reads the same way. Counts format with the invariant
/// culture like every other number in the report.
/// </summary>
internal static class ReportText
{
    /// <summary>Formats a count with its noun, adding an "s" when the count is not one.</summary>
    public static string Count(int count, string noun)
        => $"{count.ToString(CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";

    /// <summary>Formats a count with an explicit plural, for a noun that does not pluralize with "s".</summary>
    public static string Count(int count, string singular, string plural)
        => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";
}
