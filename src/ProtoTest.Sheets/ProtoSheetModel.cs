namespace ProtoTest.Sheets;

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// A sheet modelled as a record: <c>[Sheet]</c> and <c>[Column]</c> declare the layout once, the model
/// verifies it, and tests read typed rows and columns without repeating header paths. The record is the
/// model; <see cref="ProtoSheetModel{TRow}"/> remains the escape hatch for shapes a record cannot hold.
/// </summary>
public sealed class ProtoSheetModel<TRow> where TRow : notnull
{
    private readonly ProtoTable _table;
    private readonly ProtoExecutionContext? _context;
    private readonly IReadOnlyDictionary<PropertyInfo, int> _columns;

    private ProtoSheetModel(
        ProtoSheet sheet,
        ProtoTable table,
        IReadOnlyDictionary<PropertyInfo, int> columns,
        ProtoExecutionContext? context)
    {
        Sheet = sheet;
        _table = table;
        _columns = columns;
        _context = context;
    }

    public ProtoSheet Sheet { get; }

    /// <summary>The data rows, projected onto the record.</summary>
    public IReadOnlyList<TRow> Rows
    {
        get
        {
            _table.RecordRead(_table.DataRange);
            return [.. Enumerable.Range(_table.DataStartRow, _table.RowCount).Select(Project)];
        }
    }

    /// <summary>Finds the first row matching the predicate; no match fails the test.</summary>
    public TRow Row(Func<TRow, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (var row in Rows)
        {
            if (predicate(row))
            {
                return row;
            }
        }

        throw new SpreadsheetAssertionException(
            $"No row of '{Sheet.Name}' matched the predicate.");
    }

    /// <summary>Reads a whole column as typed values with property-style assertions.</summary>
    public ProtoModelColumn<TValue> Column<TValue>(Expression<Func<TRow, TValue>> property)
    {
        var info = PropertyOf(property);
        if (!_columns.TryGetValue(info, out var number))
        {
            throw new SpreadsheetAssertionException(
                $"'{typeof(TRow).Name}.{info.Name}' is not mapped to a column; mark it with a [Column(\"...\")] attribute.");
        }

        var optional = info.GetCustomAttribute<ColumnAttribute>()?.Optional == true;
        _table.RecordRead(_table.ColumnRange(number));
        var values = new TValue?[_table.RowCount];
        for (var index = 0; index < values.Length; index++)
        {
            var cell = _table.Cell(_table.DataStartRow + index, number);
            GuardEmpty(info, cell, optional);
            values[index] = (TValue?)ConvertValue(typeof(TValue), cell);
        }

        return new ProtoModelColumn<TValue>(Sheet.Name, info.Name, values, _table.DataStartRow, _context);
    }

    /// <summary>Checks every declared column against the record's shape; all violations are reported.</summary>
    public void Verify()
    {
        _table.RecordRead(_table.DataRange);
        var failures = new List<string>();
        foreach (var (property, number) in _columns)
        {
            var column = property.GetCustomAttribute<ColumnAttribute>()!;
            var optional = column.Optional;
            var seen = column.Unique ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
            for (var row = _table.DataStartRow; row < _table.DataStartRow + Math.Max(0, _table.RowCount); row++)
            {
                var cell = _table.Cell(row, number);
                if (cell.IsEmpty)
                {
                    if (!optional && !IsNullable(property.PropertyType))
                    {
                        failures.Add($"'{property.Name}' is empty at {cell.Reference}");
                    }

                    continue;
                }

                if (!TryConvert(property.PropertyType, cell))
                {
                    failures.Add($"'{property.Name}' is not a {property.PropertyType.Name} at {cell.Reference} (was {cell.Display()})");
                    continue;
                }

                // Constraints compare the typed value: a date cell has no Number, and a numeric cell
                // has no Text, so validating only those would silently skip the constraint.
                if (!double.IsNaN(column.Min) && TypedNumber(cell) is { } below && below < column.Min)
                {
                    failures.Add($"'{property.Name}' is {cell.Display()} at {cell.Reference}, below the minimum {column.Min}");
                }

                if (!double.IsNaN(column.Max) && TypedNumber(cell) is { } above && above > column.Max)
                {
                    failures.Add($"'{property.Name}' is {cell.Display()} at {cell.Reference}, above the maximum {column.Max}");
                }

                if (column.Pattern is { } pattern
                    && cell.RenderedValue is { } rendered
                    && !System.Text.RegularExpressions.Regex.IsMatch(rendered, pattern))
                {
                    failures.Add($"'{property.Name}' is '{rendered}' at {cell.Reference}, which does not match '{pattern}'");
                }

                if (column.OneOf is { Length: > 0 } allowed
                    && cell.RenderedValue is { } candidate
                    && !allowed.Contains(candidate, StringComparer.Ordinal))
                {
                    failures.Add($"'{property.Name}' is '{candidate}' at {cell.Reference}, not one of {string.Join(", ", allowed)}");
                }

                if (seen is not null)
                {
                    // The key carries the kind: text "1200" and the number 1200 are different values
                    // even though both render as "1200".
                    var uniqueKey = UniqueKey(cell);
                    if (seen.TryGetValue(uniqueKey, out var firstReference))
                    {
                        failures.Add($"'{property.Name}' repeats '{cell.Display()}' at {cell.Reference} (first at {firstReference})");
                    }
                    else
                    {
                        seen[uniqueKey] = cell.Reference;
                    }
                }
            }
        }

        using var operation = _context?.Trace
            .Operation("sheets.model", $"Sheets · model {typeof(TRow).Name}", "ProtoTest.Sheets")
            .With("sheets.sheet", Sheet.Name)
            .With("sheets.columns", _columns.Count.ToString(CultureInfo.InvariantCulture))
            .Begin();
        if (failures.Count == 0)
        {
            operation?.Succeed();
            return;
        }

        var shown = failures.Take(10).ToArray();
        var exception = new SpreadsheetAssertionException(
            $"'{Sheet.Name}' does not match {typeof(TRow).Name}: {string.Join("; ", shown)}" +
            (failures.Count > shown.Length ? $" (+{failures.Count - shown.Length} more)" : string.Empty) + ".");
        operation?.Fail(exception);
        throw exception;
    }

    internal static ProtoSheetModel<TRow> Read(ProtoWorkbook workbook, ProtoExecutionContext? context)
    {
        var attribute = typeof(TRow).GetCustomAttribute<SheetAttribute>()
            ?? throw new SpreadsheetAssertionException(
                $"{typeof(TRow).Name} needs a [Sheet(\"...\")] attribute to model a sheet.");
        var sheet = workbook.Sheet(attribute.Name);
        var table = sheet.Table(attribute.HeaderRows);
        var columns = new Dictionary<PropertyInfo, int>();
        foreach (var property in typeof(TRow).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<ColumnAttribute>() is { } column)
            {
                if (column.Optional && !IsNullable(property.PropertyType))
                {
                    throw new SpreadsheetAssertionException(
                        $"'{property.Name}' on {typeof(TRow).Name} is marked Optional, but " +
                        $"{property.PropertyType.Name} cannot hold an empty cell. Use a nullable type " +
                        $"such as {property.PropertyType.Name}?.");
                }

                columns[property] = table.ColumnNumber(column.Path);
            }
        }

        if (columns.Count == 0)
        {
            throw new SpreadsheetAssertionException(
                $"{typeof(TRow).Name} declares no [Column] properties.");
        }

        return new ProtoSheetModel<TRow>(sheet, table, columns, context);
    }

    private TRow Project(int row)
    {
        // Records only expose their primary constructor, so the instance is created uninitialized and
        // every declared property is set from its column.
        var instance = (TRow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TRow));
        foreach (var (property, number) in _columns)
        {
            var cell = _table.Cell(row, number);
            var optional = property.GetCustomAttribute<ColumnAttribute>()?.Optional == true;
            // The projection must fail the same way Column does; ConvertValue returns null for an
            // empty cell, and assigning null to a non-nullable value type throws a raw reflection error.
            GuardEmpty(property, cell, optional);
            property.SetValue(instance, ConvertValue(property.PropertyType, cell));
        }

        return instance;
    }

    private void GuardEmpty(PropertyInfo property, ProtoCell cell, bool optional)
    {
        if (cell.IsEmpty && !IsNullable(property.PropertyType) && !optional)
        {
            throw new SpreadsheetAssertionException(
                $"'{property.Name}' is empty at {cell.Reference}, so '{Sheet.Name}' has no " +
                $"{property.PropertyType.Name} value for it. Mark the column Optional to allow empty cells.");
        }
    }

    /// <summary>The numeric value a constraint compares: a number, or a date as its serial value.</summary>
    private static double? TypedNumber(ProtoCell cell)
        => cell.Number ?? cell.Date?.ToOADate();

    /// <summary>A uniqueness key that keeps text, numbers, booleans and dates apart.</summary>
    private static string UniqueKey(ProtoCell cell)
        => cell.Text is { } text ? $"text:{text}"
            : cell.Number is { } number ? $"number:{number.ToString("R", CultureInfo.InvariantCulture)}"
            : cell.Boolean is { } boolean ? $"boolean:{boolean}"
            : $"date:{cell.Date!.Value.ToString("O", CultureInfo.InvariantCulture)}";

    private static PropertyInfo PropertyOf<TValue>(Expression<Func<TRow, TValue>> property)
        => property.Body is MemberExpression { Member: PropertyInfo info }
            ? info
            : throw new ArgumentException("Use a property access like row => row.Amount.", nameof(property));

    private static bool TryConvert(Type type, ProtoCell cell)
    {
        try
        {
            return ConvertValue(type, cell) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static object? ConvertValue(Type type, ProtoCell cell)
    {
        if (cell.IsEmpty)
        {
            return null;
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return cell.Text ?? cell.Display();
        }

        if (target == typeof(decimal))
        {
            return cell.Number is { } number ? (decimal)number : throw new FormatException(cell.Display());
        }

        if (target == typeof(double))
        {
            return cell.Number ?? throw new FormatException(cell.Display());
        }

        if (target == typeof(int))
        {
            return cell.Number is { } value ? (int)Math.Round(value) : throw new FormatException(cell.Display());
        }

        if (target == typeof(long))
        {
            return cell.Number is { } value ? (long)Math.Round(value) : throw new FormatException(cell.Display());
        }

        if (target == typeof(bool))
        {
            return cell.Boolean ?? throw new FormatException(cell.Display());
        }

        if (target == typeof(DateTime))
        {
            return cell.Date ?? throw new FormatException(cell.Display());
        }

        throw new FormatException(
            $"'{cell.Display()}' at {cell.Reference} cannot be converted to {target.Name}.");
    }

    private static bool IsNullable(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
