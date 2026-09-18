namespace ProtoTest.Sheets;

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// A sheet modelled as a record: <c>[Sheet]</c> and <c>[Column]</c> declare the layout once, the model
/// verifies it, and tests read typed rows and columns without repeating header paths. The record is the
/// model; <see cref="ProtoSheetModel"/> classes remain the escape hatch for shapes a record cannot hold.
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
        => [.. Enumerable.Range(_table.DataStartRow, _table.RowCount).Select(Project)];

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
        var number = _columns[info];
        var values = Enumerable.Range(_table.DataStartRow, _table.RowCount)
            .Select(row => (TValue?)ConvertValue(typeof(TValue), _table.Cell(row, number)))
            .ToArray();
        return new ProtoModelColumn<TValue>(Sheet.Name, info.Name, values, _table.DataStartRow, _context);
    }

    /// <summary>Checks every declared column against the record's shape; all violations are reported.</summary>
    public void Verify()
    {
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

                if (!double.IsNaN(column.Min) && cell.Number is { } below && below < column.Min)
                {
                    failures.Add($"'{property.Name}' is {below} at {cell.Reference}, below the minimum {column.Min}");
                }

                if (!double.IsNaN(column.Max) && cell.Number is { } above && above > column.Max)
                {
                    failures.Add($"'{property.Name}' is {above} at {cell.Reference}, above the maximum {column.Max}");
                }

                if (column.Pattern is { } pattern
                    && cell.Text is { } text
                    && !System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
                {
                    failures.Add($"'{property.Name}' is '{text}' at {cell.Reference}, which does not match '{pattern}'");
                }

                if (column.OneOf is { Length: > 0 } allowed
                    && cell.Text is { } candidate
                    && !allowed.Contains(candidate, StringComparer.Ordinal))
                {
                    failures.Add($"'{property.Name}' is '{candidate}' at {cell.Reference}, not one of {string.Join(", ", allowed)}");
                }

                if (seen is not null && cell.Text is { } uniqueValue)
                {
                    if (seen.TryGetValue(uniqueValue, out var firstReference))
                    {
                        failures.Add($"'{property.Name}' repeats '{uniqueValue}' at {cell.Reference} (first at {firstReference})");
                    }
                    else
                    {
                        seen[uniqueValue] = cell.Reference;
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
            property.SetValue(instance, ConvertValue(property.PropertyType, _table.Cell(row, number)));
        }

        return instance;
    }

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
