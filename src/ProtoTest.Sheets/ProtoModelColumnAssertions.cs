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

    /// <summary>
    /// Compares the column's values against the expected sequence, top to bottom, and returns the column.
    /// </summary>
    public ProtoModelColumn<TValue> Be(IReadOnlyList<TValue?> expected)
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
        return _column;
    }

    /// <summary>
    /// Checks every value against a property, for example every amount above zero, and returns the
    /// column. A negated assertion passes when at least one value does not match.
    /// </summary>
    public ProtoModelColumn<TValue> All(Func<TValue?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        string? actual = null;
        SheetAssertion.Run(
            _context,
            _column.Title,
            _column.ColumnAttributes,
            _negated,
            () =>
            {
                for (var index = 0; index < _column.Values.Count; index++)
                {
                    if (!predicate(_column.Values[index]))
                    {
                        actual = $"row {_dataStartRow + index} was {ProtoModelColumn<TValue>.Display(_column.Values[index])}";
                        return false;
                    }
                }

                return true;
            },
            () => _negated
                ? new SheetAssertionFailure(
                    $"{SheetAssertion.Describe(Subject, "hold only matching values", true)} but it did.")
                : new SheetAssertionFailure(
                    $"{SheetAssertion.Describe(Subject, "hold only matching values", false)} but {actual}.",
                    new Dictionary<string, string?>
                    {
                        ["sheets.expected"] = "hold only matching values",
                        ["sheets.actual"] = actual
                    }));
        return _column;
    }

    /// <summary>Checks the values are ordered in the requested direction. Returns the column.</summary>
    public ProtoModelColumn<TValue> BeSortedBy(ProtoSortDirection direction = ProtoSortDirection.Ascending)
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
                    if (direction == ProtoSortDirection.Ascending ? comparison > 0 : comparison < 0)
                    {
                        actual = $"row {_dataStartRow + index} was {ProtoModelColumn<TValue>.Display(_column.Values[index])}";
                        return false;
                    }
                }

                return true;
            },
            () =>
            {
                var expectation = $"be sorted {Describe(direction)}";
                var detail = actual ?? $"it was sorted {Describe(direction)}";
                return new SheetAssertionFailure(
                    $"{SheetAssertion.Describe(Subject, expectation, _negated)} but {detail}.",
                    new Dictionary<string, string?>
                    {
                        ["sheets.expected"] = expectation,
                        ["sheets.actual"] = detail
                    });
            });
        return _column;
    }

    private static string Describe(ProtoSortDirection direction)
        => direction == ProtoSortDirection.Ascending ? "ascending" : "descending";

    private string Subject => $"column '{_sheetName}.{_column.Header}'";

    private string DescribeFailure(IReadOnlyList<TValue?> expected, SheetColumnMismatch? mismatch)
    {
        if (mismatch is not { } difference)
        {
            return $"{SheetAssertion.Describe(
                Subject, $"match the expected {SheetAssertionText.Count(expected.Count)}", _negated)} but it did.";
        }

        var expectation = difference.IsCountMismatch
            ? $"have {difference.ExpectedCount} values"
            : $"have {difference.Expected} at row {_dataStartRow + difference.Index}";
        var actual = difference.IsCountMismatch
            ? $"it has {difference.ActualCount}"
            : $"it was {difference.Actual}";
        return $"{SheetAssertion.Describe(Subject, expectation, _negated)} but {actual}.";
    }
}
