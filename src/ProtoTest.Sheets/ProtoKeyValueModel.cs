namespace ProtoTest.Sheets;

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// A sheet modelled as label/value pairs: <c>[Sheet("Summary", Kind = ProtoSheetKind.KeyValue)]</c>
/// names the sheet and <c>[Label("Total")]</c> declares one label per property. Labels are read from
/// the sheet's first column and values from the second; a missing or duplicated label fails the read
/// naming the labels the sheet carries, so tests read typed values without repeating label strings.
/// </summary>
public sealed class ProtoKeyValueModel<TModel> where TModel : notnull
{
    /// <summary>The column the labels of a key-value sheet sit in.</summary>
    private const int LabelColumn = 1;

    /// <summary>The column the values of a key-value sheet sit in.</summary>
    private const int ValueColumn = 2;

    private readonly ProtoSheet _sheet;
    private readonly IReadOnlyList<SheetLabelBinding> _bindings;
    private readonly ProtoExecutionContext? _context;

    private ProtoKeyValueModel(
        ProtoSheet sheet,
        IReadOnlyList<SheetLabelBinding> bindings,
        IReadOnlyList<string> labels,
        ProtoExecutionContext? context)
    {
        _sheet = sheet;
        _bindings = bindings;
        Labels = labels;
        _context = context;
    }

    public ProtoSheet Sheet => _sheet;

    /// <summary>The labels the sheet carries, in row order.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>Reads the value under the label the property declares.</summary>
    public ProtoKeyValueEntry<TValue> Column<TValue>(Expression<Func<TModel, TValue>> property)
    {
        var binding = BindingOf(SheetPropertyExpression.PropertyOf(property));
        _sheet.RecordRead(ValueRange);
        var cell = _sheet.CellByNumber(binding.Row, ValueColumn);
        GuardEmpty(binding, cell);
        var value = (TValue?)SheetCellValue.Convert(typeof(TValue), cell);
        return new ProtoKeyValueEntry<TValue>(_sheet.Name, binding.Label, cell, value, _context);
    }

    /// <summary>The assertions of this model, for example <c>Should.MatchModel()</c>.</summary>
    public ProtoKeyValueModelAssertions<TModel> Should => new(this, negated: false);

    /// <summary>The negated assertions of this model, for example <c>ShouldNot.MatchModel()</c>.</summary>
    public ProtoKeyValueModelAssertions<TModel> ShouldNot => new(this, negated: true);

    internal ProtoExecutionContext? Context => _context;

    /// <summary>The label/value area the model reads, used to record a read of the block.</summary>
    internal string ValueRange
        => _sheet.RowCount == 0 ? _sheet.Name : $"{_sheet.Name}!A1:B{_sheet.RowCount}";

    internal ProtoKeyValueModel<TModel> AssertModel(bool negated)
    {
        _sheet.RecordRead(ValueRange);
        var failures = new List<string>();
        foreach (var binding in _bindings)
        {
            var cell = _sheet.CellByNumber(binding.Row, ValueColumn);
            if (cell.IsEmpty)
            {
                if (!binding.Optional && !binding.IsNullable)
                {
                    failures.Add($"'{binding.Label}' is empty at {cell.Reference}");
                }

                continue;
            }

            SheetColumnRules.Check(binding.Rules, binding.Property.PropertyType, binding.Label, cell, failures);
        }

        var matches = failures.Count == 0;
        SpreadsheetAssertionException? failure = null;
        if (!matches)
        {
            var shown = failures.Take(10).ToArray();
            failure = new SpreadsheetAssertionException(
                $"'{_sheet.Name}' does not match {typeof(TModel).Name}: {string.Join("; ", shown)}" +
                (failures.Count > shown.Length ? $" (+{failures.Count - shown.Length} more)" : string.Empty) + ".");
        }

        using var operation = _context?.Trace
            .Operation("sheets.model", $"Sheets · model {typeof(TModel).Name}", ProtoSheets.TraceSource)
            .With("sheets.sheet", _sheet.Name)
            .With("sheets.labels", _bindings.Count.ToString(CultureInfo.InvariantCulture))
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
            $"Expected '{_sheet.Name}' not to match {typeof(TModel).Name} but it did.");
    }

    internal static ProtoKeyValueModel<TModel> Read(ProtoWorkbook workbook, ProtoExecutionContext? context)
    {
        var attribute = typeof(TModel).GetCustomAttribute<SheetAttribute>()
            ?? throw new SpreadsheetAssertionException(
                $"{typeof(TModel).Name} needs a [Sheet(\"...\")] attribute to model a sheet.");
        if (attribute.Kind != ProtoSheetKind.KeyValue)
        {
            throw new SpreadsheetAssertionException(
                $"{typeof(TModel).Name} declares [Sheet(\"{attribute.Name}\", Kind = ProtoSheetKind.{attribute.Kind})]; " +
                $"read it with Model<{typeof(TModel).Name}>() as a table of rows.");
        }

        var sheet = workbook.Sheet(attribute.Name);
        var labels = ReadLabels(sheet);
        var bindings = new List<SheetLabelBinding>();
        foreach (var property in typeof(TModel)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(property => property.MetadataToken))
        {
            var label = property.GetCustomAttribute<LabelAttribute>();
            if (label is null)
            {
                if (property.GetCustomAttribute<ColumnAttribute>() is not null)
                {
                    throw new SpreadsheetAssertionException(
                        $"'{property.Name}' on {typeof(TModel).Name} declares [Column], but a key-value model " +
                        "maps its properties with [Label(\"...\")]. A [Column] binds a header path in a table model.");
                }

                continue;
            }

            var text = label.Label;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new SpreadsheetAssertionException(
                    $"'{property.Name}' on {typeof(TModel).Name} declares an empty label; a key-value label " +
                    "is a non-empty text, for example [Label(\"Total\")].");
            }

            var matches = labels
                .Where(candidate => string.Equals(candidate.Label, text, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new SpreadsheetAssertionException(
                    $"The sheet '{sheet.Name}' has no label '{text}'. It has: " +
                    (labels.Count == 0 ? "<none>" : string.Join(", ", labels.Select(label => label.Label))) + ".");
            }

            if (matches.Length > 1)
            {
                throw new SpreadsheetAssertionException(
                    $"The sheet '{sheet.Name}' carries the label '{text}' more than once " +
                    $"(rows {string.Join(" and ", matches.Select(match => match.Row))}); a key-value label " +
                    "names one value.");
            }

            if (label.Optional && !SheetCellValue.IsNullable(property.PropertyType))
            {
                throw new SpreadsheetAssertionException(
                    $"'{property.Name}' on {typeof(TModel).Name} is marked Optional, but " +
                    $"{property.PropertyType.Name} cannot hold an empty cell. Use a nullable type " +
                    $"such as {property.PropertyType.Name}?.");
            }

            bindings.Add(new SheetLabelBinding(property, matches[0].Row, text, label));
        }

        if (bindings.Count == 0)
        {
            throw new SpreadsheetAssertionException(
                $"{typeof(TModel).Name} declares no [Label] properties.");
        }

        return new ProtoKeyValueModel<TModel>(
            sheet,
            bindings,
            [.. labels.Select(label => label.Label)],
            context);
    }

    /// <summary>The non-empty label cells of the sheet's first column, in row order.</summary>
    private static List<(string Label, int Row)> ReadLabels(ProtoSheet sheet)
    {
        var labels = new List<(string Label, int Row)>();
        for (var row = 1; row <= sheet.RowCount; row++)
        {
            var text = sheet.CellByNumber(row, LabelColumn).RenderedValue;
            if (!string.IsNullOrWhiteSpace(text))
            {
                labels.Add((text, row));
            }
        }

        return labels;
    }

    private SheetLabelBinding BindingOf(PropertyInfo property)
        => _bindings.FirstOrDefault(binding => binding.Property == property)
            ?? throw new SpreadsheetAssertionException(
                $"'{typeof(TModel).Name}.{property.Name}' is not mapped to a label; mark it with a " +
                "[Label(\"...\")] attribute.");

    private void GuardEmpty(SheetLabelBinding binding, ProtoCell cell)
    {
        if (cell.IsEmpty && !binding.IsNullable && !binding.Optional)
        {
            throw new SpreadsheetAssertionException(
                $"'{binding.Label}' is empty at {cell.Reference}, so '{_sheet.Name}' has no " +
                $"{binding.Property.PropertyType.Name} value for it. Mark the label Optional to allow " +
                "empty cells.");
        }
    }
}
