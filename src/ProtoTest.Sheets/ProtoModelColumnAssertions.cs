namespace ProtoTest.Sheets;

using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> surface of a typed model column. The comparison is written once
/// and runs with the polarity of the property that produced this facade.
/// </summary>
public sealed class ProtoModelColumnAssertions<TValue>
{
    private readonly ProtoModelColumn<TValue> _column;
    private readonly string _sheetName;
    private readonly int _dataStartRow;
    private readonly ProtoExecutionContext? _context;
    private readonly bool _negated;

    internal ProtoModelColumnAssertions(
        ProtoModelColumn<TValue> column,
        string sheetName,
        int dataStartRow,
        ProtoExecutionContext? context,
        bool negated)
    {
        _column = column;
        _sheetName = sheetName;
        _dataStartRow = dataStartRow;
        _context = context;
        _negated = negated;
    }

    /// <summary>Compares the column's values against the expected sequence, top to bottom.</summary>
    public void Be(IReadOnlyList<TValue?> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        SheetColumnMismatch? mismatch = null;
        SheetAssertion.Run(
            _context,
            _column.Title,
            _column.ColumnAttributes,
            _negated,
            () => (mismatch = SheetColumnAssertion.FindMismatch(
                _column.Values,
                expected,
                EqualityComparer<TValue?>.Default,
                ProtoModelColumn<TValue>.Display)) is null,
            () => new SheetAssertionFailure(DescribeFailure(expected, mismatch)));
    }

    /// <summary>Checks the values are ordered.</summary>
    public void BeSortedBy(bool ascending = true)
    {
        string? actual = null;
        SheetAssertion.Run(
            _context,
            _column.Title,
            _column.ColumnAttributes,
            _negated,
            () =>
            {
                for (var index = 1; index < _column.Values.Count; index++)
                {
                    var comparison = Comparer<TValue?>.Default.Compare(
                        _column.Values[index - 1], _column.Values[index]);
                    if (ascending ? comparison > 0 : comparison < 0)
                    {
                        actual = $"row {_dataStartRow + index} was {ProtoModelColumn<TValue>.Display(_column.Values[index])}";
                        return false;
                    }
                }

                return true;
            },
            () =>
            {
                var expectation = $"be sorted {(ascending ? "ascending" : "descending")}";
                var detail = actual ?? $"it was sorted {(ascending ? "ascending" : "descending")}";
                return new SheetAssertionFailure(
                    $"{SheetAssertion.Describe(Subject, expectation, _negated)} but {detail}.",
                    new Dictionary<string, string?>
                    {
                        ["sheets.expected"] = expectation,
                        ["sheets.actual"] = detail
                    });
            });
    }

    private string Subject => $"column '{_sheetName}.{_column.Header}'";

    private string DescribeFailure(IReadOnlyList<TValue?> expected, SheetColumnMismatch? mismatch)
    {
        if (mismatch is not { } difference)
        {
            return $"{SheetAssertion.Describe(
                Subject, $"match the expected {Count(expected.Count)}", _negated)} but it did.";
        }

        var expectation = difference.IsCountMismatch
            ? $"have {difference.ExpectedCount} values"
            : $"have {difference.Expected} at row {_dataStartRow + difference.Index}";
        var actual = difference.IsCountMismatch
            ? $"it has {difference.ActualCount}"
            : $"it was {difference.Actual}";
        return $"{SheetAssertion.Describe(Subject, expectation, _negated)} but {actual}.";
    }

    private static string Count(int count) => $"{count} {(count == 1 ? "value" : "values")}";
}
