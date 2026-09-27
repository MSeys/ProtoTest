namespace ProtoTest.Sheets.Internal;

using System.Text.RegularExpressions;

/// <summary>
/// The one place a non-empty declared cell is checked against its <c>[Column]</c> attribute:
/// conversion to the property type plus the <c>Min</c>, <c>Max</c>, <c>Pattern</c> and <c>OneOf</c>
/// constraints. A table model runs it per data row; a key-value model runs it for the value under a label.
/// </summary>
internal static class SheetColumnRules
{
    /// <summary>
    /// Checks one non-empty cell and appends every violation. Returns false when the value does not
    /// convert to the property type; the constraints compare the typed value, so they are skipped then.
    /// </summary>
    public static bool Check(
        ColumnAttribute column,
        Type propertyType,
        string name,
        ProtoCell cell,
        ICollection<string> failures)
    {
        if (!SheetCellValue.TryConvert(propertyType, cell, out _))
        {
            failures.Add($"'{name}' is not a {propertyType.Name} at {cell.Reference} (was {cell.Display()})");
            return false;
        }

        // Constraints compare the typed value: a date cell has no Number, and a numeric cell
        // has no Text, so validating only those would silently skip the constraint.
        if (!double.IsNaN(column.Min) && TypedNumber(cell) is { } below && below < column.Min)
        {
            failures.Add($"'{name}' is {cell.Display()} at {cell.Reference}, below the minimum {column.Min}");
        }

        if (!double.IsNaN(column.Max) && TypedNumber(cell) is { } above && above > column.Max)
        {
            failures.Add($"'{name}' is {cell.Display()} at {cell.Reference}, above the maximum {column.Max}");
        }

        if (column.Pattern is { } pattern
            && cell.RenderedValue is { } rendered
            && !Regex.IsMatch(rendered, pattern))
        {
            failures.Add($"'{name}' is '{rendered}' at {cell.Reference}, which does not match '{pattern}'");
        }

        if (column.OneOf is { Length: > 0 } allowed
            && cell.RenderedValue is { } candidate
            && !allowed.Contains(candidate, StringComparer.Ordinal))
        {
            failures.Add($"'{name}' is '{candidate}' at {cell.Reference}, not one of {string.Join(", ", allowed)}");
        }

        return true;
    }

    /// <summary>The numeric value a constraint compares: a number, or a date as its serial value.</summary>
    private static double? TypedNumber(ProtoCell cell)
        => cell.Number ?? cell.Date?.ToOADate();
}
