namespace ProtoTest.Sheets;

using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> surface of a range. Each check is written once and runs with the
/// polarity of the property that produced this facade.
/// </summary>
public sealed class ProtoRangeAssertions
{
    private readonly ProtoRange _range;
    private readonly string _sheetName;
    private readonly ProtoExecutionContext? _context;
    private readonly bool _negated;

    internal ProtoRangeAssertions(
        ProtoRange range,
        string sheetName,
        ProtoExecutionContext? context,
        bool negated)
    {
        _range = range;
        _sheetName = sheetName;
        _context = context;
        _negated = negated;
    }

    /// <summary>Asserts the range has exactly this many rows and columns.</summary>
    public void HaveDimensions(int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        var reference = $"{_sheetName}!{_range.Reference}";
        SheetAssertion.Run(
            _context,
            $"Sheets · {reference}",
            new Dictionary<string, string?>
            {
                ["sheets.range"] = _range.Reference,
                ["sheets.expected"] = $"{rows}x{columns}",
                ["sheets.actual"] = $"{_range.RowCount}x{_range.ColumnCount}"
            },
            _negated,
            () => _range.RowCount == rows && _range.ColumnCount == columns,
            () => new SheetAssertionFailure(
                $"{SheetAssertion.Describe(reference, $"have dimensions {rows}x{columns}", _negated)} " +
                $"but it was {_range.RowCount}x{_range.ColumnCount}."));
    }

    /// <summary>Compares the range's text values to an expected table, row by row.</summary>
    public void Match(IReadOnlyList<IReadOnlyList<string?>> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var expectedRows = expected.Count;
        var expectedColumns = expectedRows == 0 ? 0 : expected[0].Count;
        var expectedShape = $"{expectedRows}x{expectedColumns}";
        ProtoCell? mismatchedCell = null;
        string? expectedValue = null;
        SheetAssertion.Run(
            _context,
            $"Sheets · {_sheetName}!{_range.Reference}",
            new Dictionary<string, string?>
            {
                ["sheets.range"] = _range.Reference,
                ["sheets.expected"] = expectedShape,
                ["sheets.actual"] = $"{_range.RowCount}x{_range.ColumnCount}"
            },
            _negated,
            () =>
            {
                if (_range.RowCount != expectedRows || _range.ColumnCount != expectedColumns)
                {
                    return false;
                }

                for (var row = 0; row < expectedRows; row++)
                {
                    for (var column = 0; column < expectedColumns; column++)
                    {
                        var cell = _range.Rows[row][column];
                        if (string.Equals(cell.RenderedValue, expected[row][column], StringComparison.Ordinal))
                        {
                            continue;
                        }

                        mismatchedCell = cell;
                        expectedValue = expected[row][column];
                        return false;
                    }
                }

                return true;
            },
            () => new SheetAssertionFailure(
                DescribeFailure(expectedShape, mismatchedCell, expectedValue)));
    }

    private string DescribeFailure(string expectedShape, ProtoCell? mismatchedCell, string? expectedValue)
    {
        if (mismatchedCell is not null)
        {
            return $"{SheetAssertion.Describe(
                $"{_sheetName}!{mismatchedCell.Reference}",
                $"be {Format(expectedValue)}", _negated)} but it was {mismatchedCell.Display()}.";
        }

        if (_negated)
        {
            return $"{SheetAssertion.Describe(
                $"{_sheetName}!{_range.Reference}",
                $"match the expected {expectedShape} values", _negated)} but it did.";
        }

        return $"{SheetAssertion.Describe(
            $"{_sheetName}!{_range.Reference}",
            $"have dimensions {expectedShape}", _negated)} " +
            $"but it was {_range.RowCount}x{_range.ColumnCount}.";
    }

    private static string Format(string? value) => value is null ? "empty" : $"'{value}'";
}
