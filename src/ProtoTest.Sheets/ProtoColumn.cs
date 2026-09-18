namespace ProtoTest.Sheets;

/// <summary>A table column: its header path and its data cells.</summary>
public sealed class ProtoColumn
{
    private readonly ProtoTable _table;
    private readonly int _columnNumber;

    internal ProtoColumn(ProtoTable table, int columnNumber)
    {
        _table = table;
        _columnNumber = columnNumber;
    }

    public IReadOnlyList<string> Header => _table.Headers[_columnNumber - 1];

    public IReadOnlyList<ProtoCell> Cells
        => [.. Enumerable.Range(_table.DataStartRow, _table.RowCount)
            .Select(row => _table.Cell(row, _columnNumber))];

    public IReadOnlyList<string?> Values
        => [.. Cells.Select(cell => cell.Text
            ?? cell.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? cell.Boolean?.ToString()
            ?? cell.Date?.ToString("O", System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>Compares the column's values against the expected sequence, top to bottom.</summary>
    public void ShouldBe(IReadOnlyList<string?> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        _table.RecordRead();
        var actual = Values;
        if (actual.Count != expected.Count)
        {
            throw new SpreadsheetAssertionException(
                $"Expected column '{string.Join(" / ", Header)}' to have {expected.Count} values " +
                $"but it has {actual.Count}.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(actual[index], expected[index], StringComparison.Ordinal))
            {
                throw new SpreadsheetAssertionException(
                    $"Expected column '{string.Join(" / ", Header)}' row {_table.DataStartRow + index} " +
                    $"to be {(expected[index] is null ? "empty" : $"'{expected[index]}'")} " +
                    $"but it was {(actual[index] is null ? "<empty>" : $"'{actual[index]}'")}.");
            }
        }
    }
}
