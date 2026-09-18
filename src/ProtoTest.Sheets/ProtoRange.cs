namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>A rectangular range of cells, row-major, with shape assertions.</summary>
public sealed class ProtoRange
{
    private readonly string _sheetName;
    private readonly ProtoExecutionContext? _context;

    internal ProtoRange(
        string reference,
        IReadOnlyList<IReadOnlyList<ProtoCell>> rows,
        string sheetName,
        ProtoExecutionContext? context)
    {
        Reference = reference;
        Rows = rows;
        _sheetName = sheetName;
        _context = context;
    }

    public string Reference { get; }

    public IReadOnlyList<IReadOnlyList<ProtoCell>> Rows { get; }

    public int RowCount => Rows.Count;

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Count;

    public ProtoCell this[int row, int column] => Rows[row][column];

    public void ShouldHaveDimensions(int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        Assert(
            RowCount == rows && ColumnCount == columns,
            "have dimensions",
            $"{rows}x{columns}",
            $"{RowCount}x{ColumnCount}");
    }

    /// <summary>Compares the range's text values to an expected table, row by row.</summary>
    public void ShouldMatch(IReadOnlyList<IReadOnlyList<string?>> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var expectedRows = expected.Count;
        var expectedColumns = expected.Count == 0 ? 0 : expected[0].Count;
        Assert(
            RowCount == expectedRows && ColumnCount == expectedColumns,
            "have dimensions",
            $"{expectedRows}x{expectedColumns}",
            $"{RowCount}x{ColumnCount}");
        for (var row = 0; row < expectedRows; row++)
        {
            for (var column = 0; column < expectedColumns; column++)
            {
                var cell = Rows[row][column];
                var wanted = expected[row][column];
                if (!string.Equals(cell.Text, wanted, StringComparison.Ordinal))
                {
                    throw new SpreadsheetAssertionException(
                        $"Expected {_sheetName}!{cell.Reference} to be " +
                        $"{(wanted is null ? "empty" : $"'{wanted}'")} but it was {cell.Display()}.");
                }
            }
        }

        _context?.Trace
            .Operation("sheets.assert", $"Sheets · {_sheetName}!{Reference}", "ProtoTest.Sheets")
            .With("sheets.range", Reference)
            .With("sheets.expected", $"{expectedRows}x{expectedColumns}")
            .RunAsync(() => ValueTask.CompletedTask).GetAwaiter().GetResult();
    }

    private void Assert(bool passed, string verb, string expected, string actual)
    {
        if (!passed)
        {
            throw new SpreadsheetAssertionException(
                $"Expected {_sheetName}!{Reference} to {verb} {expected} but it was {actual}.");
        }
    }
}
