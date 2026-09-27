namespace ProtoTest.Sheets;

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// A sheet modelled as a record: <c>[Sheet]</c> and <c>[Column]</c> declare the layout once, the model
/// verifies it, and tests read typed rows and columns without repeating header paths. Rows are
/// constructed through the record's primary constructor, so its guards run; <see cref="ProtoSheetModel{TRow}"/>
/// remains the escape hatch for shapes a record cannot hold.
/// </summary>
public sealed class ProtoSheetModel<TRow> where TRow : notnull
{
    private readonly ProtoTable _table;
    private readonly ProtoExecutionContext? _context;
    private readonly IReadOnlyList<SheetColumnBinding> _columns;
    private readonly ConstructorInfo? _constructor;
    private readonly IReadOnlyList<ConstructorArgument> _constructorArguments;
    private readonly IReadOnlyList<SheetColumnBinding> _propertyBindings;

    private ProtoSheetModel(
        ProtoSheet sheet,
        ProtoTable table,
        IReadOnlyList<SheetColumnBinding> columns,
        ConstructorInfo? constructor,
        IReadOnlyList<ConstructorArgument> constructorArguments,
        ProtoExecutionContext? context)
    {
        Sheet = sheet;
        _table = table;
        _columns = columns;
        _constructor = constructor;
        _constructorArguments = constructorArguments;
        _propertyBindings = constructorArguments.Count == 0
            ? columns
            : [.. columns.Where(column => constructorArguments.All(argument => argument.Binding != column))];
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
        var binding = BindingOf(SheetPropertyExpression.PropertyOf(property));
        _table.RecordRead(_table.ColumnRange(binding.Number));
        var values = new TValue?[_table.RowCount];
        for (var index = 0; index < values.Length; index++)
        {
            var cell = _table.Cell(_table.DataStartRow + index, binding.Number);
            GuardEmpty(binding, cell);
            values[index] = (TValue?)SheetCellValue.Convert(typeof(TValue), cell);
        }

        return new ProtoModelColumn<TValue>(Sheet.Name, binding.Name, values, _table.DataStartRow, _context);
    }

    /// <summary>The assertions of this model, for example <c>Should.MatchModel()</c>.</summary>
    public ProtoSheetModelAssertions<TRow> Should => new(this, negated: false);

    /// <summary>The negated assertions of this model, for example <c>ShouldNot.MatchHeaders()</c>.</summary>
    public ProtoSheetModelAssertions<TRow> ShouldNot => new(this, negated: true);

    /// <summary>Checks every declared column against the record's shape; all violations are reported.</summary>
    /// <remarks>Obsolete: use <c>Should.MatchModel()</c>.</remarks>
    [Obsolete("Use Should.MatchModel() instead.")]
    public void Verify() => AssertModel();

    internal ProtoExecutionContext? Context => _context;

    /// <summary>The header paths the record declares, in declaration order.</summary>
    internal IReadOnlyList<IReadOnlyList<string>> DeclaredHeaders
        => [.. _columns.Select(column => column.Attribute.Path)];

    /// <summary>The header paths the sheet carries, one per column, top level first.</summary>
    internal IReadOnlyList<IReadOnlyList<string>> SheetHeaders => _table.Headers;

    /// <summary>Records a read of the header rows, for coverage.</summary>
    internal void RecordHeaderRead() => _table.RecordRead(_table.HeaderRange);

    internal ProtoSheetModel<TRow> AssertModel(bool negated = false)
    {
        _table.RecordRead(_table.DataRange);
        var failures = new List<string>();
        foreach (var binding in _columns)
        {
            var column = binding.Attribute;
            var seen = column.Unique ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
            for (var row = _table.DataStartRow; row < _table.DataStartRow + Math.Max(0, _table.RowCount); row++)
            {
                var cell = _table.Cell(row, binding.Number);
                if (cell.IsEmpty)
                {
                    if (!binding.Optional && !binding.IsNullable)
                    {
                        failures.Add($"'{binding.Name}' is empty at {cell.Reference}");
                    }

                    continue;
                }

                if (!SheetColumnRules.Check(column, binding.Property.PropertyType, binding.Name, cell, failures))
                {
                    continue;
                }

                if (seen is not null)
                {
                    // The key carries the kind: text "1200" and the number 1200 are different values
                    // even though both render as "1200".
                    var uniqueKey = UniqueKey(cell);
                    if (seen.TryGetValue(uniqueKey, out var firstReference))
                    {
                        failures.Add($"'{binding.Name}' repeats '{cell.Display()}' at {cell.Reference} (first at {firstReference})");
                    }
                    else
                    {
                        seen[uniqueKey] = cell.Reference;
                    }
                }
            }
        }

        var matches = failures.Count == 0;
        SpreadsheetAssertionException? failure = null;
        if (!matches)
        {
            var shown = failures.Take(10).ToArray();
            failure = new SpreadsheetAssertionException(
                $"'{Sheet.Name}' does not match {typeof(TRow).Name}: {string.Join("; ", shown)}" +
                (failures.Count > shown.Length ? $" (+{failures.Count - shown.Length} more)" : string.Empty) + ".");
        }

        using var operation = _context?.Trace
            .Operation("sheets.model", $"Sheets · model {typeof(TRow).Name}", ProtoSheets.TraceSource)
            .With("sheets.sheet", Sheet.Name)
            .With("sheets.columns", _columns.Count.ToString(CultureInfo.InvariantCulture))
            .Begin();
        if (failure is null)
        {
            operation?.Succeed();
        }
        else
        {
            operation?.Fail(failure);
        }

        // The operation records whether the sheet matched; the facade's polarity decides whether that
        // outcome is the assertion the test asked for.
        if ((failure is null) != negated)
        {
            return this;
        }

        throw failure ?? new SpreadsheetAssertionException(
            $"Expected '{Sheet.Name}' not to match {typeof(TRow).Name} but it did.");
    }

    internal static ProtoSheetModel<TRow> Read(ProtoWorkbook workbook, ProtoExecutionContext? context)
    {
        var attribute = typeof(TRow).GetCustomAttribute<SheetAttribute>()
            ?? throw new SpreadsheetAssertionException(
                $"{typeof(TRow).Name} needs a [Sheet(\"...\")] attribute to model a sheet.");
        if (attribute.Kind != ProtoSheetKind.Table)
        {
            throw new SpreadsheetAssertionException(
                $"{typeof(TRow).Name} declares [Sheet(\"{attribute.Name}\", Kind = ProtoSheetKind.{attribute.Kind})]; " +
                $"read it with KeyValueModel<{typeof(TRow).Name}>() for a label/value sheet.");
        }

        var sheet = workbook.Sheet(attribute.Name);
        var table = sheet.Table(attribute.HeaderRows);
        var columns = new List<SheetColumnBinding>();
        // A record's properties arrive in metadata order - its declaration order - which is the order
        // MatchHeaders compares the sheet's header row against.
        foreach (var property in typeof(TRow)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(property => property.MetadataToken))
        {
            if (property.GetCustomAttribute<ColumnAttribute>() is not { } column)
            {
                continue;
            }

            var binding = new SheetColumnBinding(property, table.ColumnNumber(column.Path), column);
            if (binding.Optional && !binding.IsNullable)
            {
                throw new SpreadsheetAssertionException(
                    $"'{binding.Name}' on {typeof(TRow).Name} is marked Optional, but " +
                    $"{property.PropertyType.Name} cannot hold an empty cell. Use a nullable type " +
                    $"such as {property.PropertyType.Name}?.");
            }

            columns.Add(binding);
        }

        if (columns.Count == 0)
        {
            throw new SpreadsheetAssertionException(
                $"{typeof(TRow).Name} declares no [Column] properties.");
        }

        var constructor = ResolveConstructor(columns, out var constructorArguments);
        return new ProtoSheetModel<TRow>(sheet, table, columns, constructor, constructorArguments, context);
    }

    /// <summary>
    /// Resolves the route a row is constructed through. A parameterless constructor is used as-is; a
    /// record's primary constructor is used when every parameter maps to a <c>[Column]</c>, so its
    /// guards and normalization run. A constructor parameter no column maps fails the model by name
    /// instead of constructing the record uninitialized with a fabricated default for that parameter.
    /// </summary>
    private static ConstructorInfo? ResolveConstructor(
        IReadOnlyList<SheetColumnBinding> columns,
        out IReadOnlyList<ConstructorArgument> arguments)
    {
        arguments = [];
        var type = typeof(TRow);
        if (type.IsValueType)
        {
            return null;
        }

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
        {
            throw new SpreadsheetAssertionException(
                $"'{type.Name}' has no public constructor, so a row cannot be constructed for it.");
        }

        if (constructors.Any(constructor => constructor.GetParameters().Length == 0))
        {
            return null;
        }

        foreach (var constructor in constructors)
        {
            var mapped = new List<ConstructorArgument>();
            foreach (var parameter in constructor.GetParameters())
            {
                var binding = columns.FirstOrDefault(column =>
                    string.Equals(column.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (binding is null)
                {
                    mapped.Clear();
                    break;
                }

                mapped.Add(new ConstructorArgument(binding, parameter.ParameterType));
            }

            if (mapped.Count == constructor.GetParameters().Length)
            {
                arguments = mapped;
                return constructor;
            }
        }

        var candidate = constructors[0];
        var unmapped = candidate.GetParameters().First(parameter =>
            columns.All(column => !string.Equals(column.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)));
        throw new SpreadsheetAssertionException(
            $"'{type.Name}' has a constructor parameter '{unmapped.Name}' that no [Column] maps, so a row " +
            "cannot be constructed through it. Mark it with [Column(\"...\")] or add a parameterless constructor.");
    }

    private TRow Project(int row)
    {
        var instance = _constructor is null ? Activator.CreateInstance<TRow>() : InvokeConstructor(row);
        foreach (var binding in _propertyBindings)
        {
            var cell = _table.Cell(row, binding.Number);
            // The projection must fail the same way Column does; the converter returns null for an
            // empty cell, and assigning null to a non-nullable value type throws a raw reflection error.
            GuardEmpty(binding, cell);
            binding.Property.SetValue(instance, SheetCellValue.Convert(binding.Property.PropertyType, cell));
        }

        return instance;
    }

    private TRow InvokeConstructor(int row)
    {
        var arguments = new object?[_constructorArguments.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = _constructorArguments[index];
            var cell = _table.Cell(row, argument.Binding.Number);
            GuardEmpty(argument.Binding, cell);
            arguments[index] = SheetCellValue.Convert(argument.ParameterType, cell);
        }

        try
        {
            return (TRow)_constructor!.Invoke(arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // A guard the model's constructor declares is the failure: the caller sees its type and
            // message instead of a reflection wrapper.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>One constructor parameter and the column its value comes from.</summary>
    private sealed record ConstructorArgument(SheetColumnBinding Binding, Type ParameterType);

    private void GuardEmpty(SheetColumnBinding binding, ProtoCell cell)
    {
        if (cell.IsEmpty && !binding.IsNullable && !binding.Optional)
        {
            throw new SpreadsheetAssertionException(
                $"'{binding.Name}' is empty at {cell.Reference}, so '{Sheet.Name}' has no " +
                $"{binding.Property.PropertyType.Name} value for it. Mark the column Optional to allow empty cells.");
        }
    }

    private SheetColumnBinding BindingOf(PropertyInfo property)
        => _columns.FirstOrDefault(binding => binding.Property == property)
            ?? throw new SpreadsheetAssertionException(
                $"'{typeof(TRow).Name}.{property.Name}' is not mapped to a column; mark it with a [Column(\"...\")] attribute.");

    /// <summary>A uniqueness key that keeps text, numbers, booleans and dates apart.</summary>
    private static string UniqueKey(ProtoCell cell)
        => cell.Text is { } text ? $"text:{text}"
            : cell.Number is { } number ? $"number:{number.ToString("R", CultureInfo.InvariantCulture)}"
            : cell.Boolean is { } boolean ? $"boolean:{boolean}"
            : $"date:{cell.Date!.Value.ToString("O", CultureInfo.InvariantCulture)}";
}
