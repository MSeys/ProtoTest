namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>A typed model column with property-style assertions over its values.</summary>
public sealed class ProtoModelColumn<TValue>
{
    private readonly ProtoExecutionContext? _context;
    private readonly string _sheetName;
    private readonly int _dataStartRow;

    internal ProtoModelColumn(
        string sheetName,
        string header,
        IReadOnlyList<TValue?> values,
        int dataStartRow,
        ProtoExecutionContext? context)
    {
        _sheetName = sheetName;
        Header = header;
        Values = values;
        _dataStartRow = dataStartRow;
        _context = context;
    }

    public string Header { get; }

    public IReadOnlyList<TValue?> Values { get; }

    /// <summary>Compares the column's values against the expected sequence, top to bottom.</summary>
    public void ShouldBe(IReadOnlyList<TValue?> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Values.Count != expected.Count)
        {
            Fail($"have {expected.Count} values", $"it has {Values.Count}");
            return;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!EqualityComparer<TValue?>.Default.Equals(Values[index], expected[index]))
            {
                Fail(
                    $"have {Display(expected[index])} at row {_dataStartRow + index}",
                    $"it was {Display(Values[index])}");
                return;
            }
        }

        Pass();
    }

    /// <summary>Checks every value against a property, for example every amount above zero.</summary>
    public void ShouldAll(Func<TValue?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        for (var index = 0; index < Values.Count; index++)
        {
            if (!predicate(Values[index]))
            {
                Fail($"hold only matching values", $"row {_dataStartRow + index} was {Display(Values[index])}");
                return;
            }
        }

        Pass();
    }

    /// <summary>Checks the values are ordered.</summary>
    public void ShouldBeSortedBy(bool ascending = true)
    {
        for (var index = 1; index < Values.Count; index++)
        {
            var comparison = Comparer<TValue?>.Default.Compare(Values[index - 1], Values[index]);
            if (ascending ? comparison > 0 : comparison < 0)
            {
                Fail(
                    $"be sorted {(ascending ? "ascending" : "descending")}",
                    $"row {_dataStartRow + index} was {Display(Values[index])}");
                return;
            }
        }

        Pass();
    }

    private void Pass()
    {
        using var operation = _context?.Trace
            .Operation("sheets.assert", $"Sheets · {_sheetName}.{Header}", "ProtoTest.Sheets")
            .With("sheets.column", $"{_sheetName}.{Header}")
            .Begin();
        operation?.Succeed();
    }

    private void Fail(string expected, string actual)
    {
        using var operation = _context?.Trace
            .Operation("sheets.assert", $"Sheets · {_sheetName}.{Header}", "ProtoTest.Sheets")
            .With("sheets.column", $"{_sheetName}.{Header}")
            .With("sheets.expected", expected)
            .With("sheets.actual", actual)
            .Begin();
        var exception = new SpreadsheetAssertionException(
            $"Expected column '{_sheetName}.{Header}' to {expected} but {actual}.");
        operation?.Fail(exception);
        throw exception;
    }

    private static string Display(TValue? value)
        => value?.ToString() ?? "<empty>";
}
