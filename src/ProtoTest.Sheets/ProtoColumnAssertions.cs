namespace ProtoTest.Sheets;

using ProtoTest.Sheets.Internal;

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> surface of a table column. The comparison is written once and
/// runs with the polarity of the property that produced this facade.
/// </summary>
public sealed class ProtoColumnAssertions
{
    private readonly ProtoColumn _column;
    private readonly ProtoTable _table;
    private readonly bool _negated;

    internal ProtoColumnAssertions(ProtoColumn column, ProtoTable table, bool negated)
    {
        _column = column;
        _table = table;
        _negated = negated;
    }

    /// <summary>Compares the column's values against the expected sequence, top to bottom.</summary>
    public void Be(IReadOnlyList<string?> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var header = string.Join(" / ", _column.Header);
        SheetColumnMismatch? mismatch = null;
        SheetAssertion.Run(
            _table.Context,
            $"Sheets · column {header}",
            new Dictionary<string, string?>
            {
                ["sheets.header"] = header,
                ["sheets.expected"] = $"{expected.Count} values"
            },
            _negated,
            () => (mismatch = SheetColumnAssertion.FindMismatch(
                _column.Values, expected, StringComparer.Ordinal, static value => value)) is null,
            () => new SheetAssertionFailure(DescribeFailure(header, expected, mismatch)));
    }

    private string DescribeFailure(string header, IReadOnlyList<string?> expected, SheetColumnMismatch? mismatch)
    {
        if (mismatch is not { } difference)
        {
            return $"{SheetAssertion.Describe(
                $"column '{header}'", $"match the expected {Count(expected.Count)}", _negated)} but it did.";
        }

        if (difference.IsCountMismatch)
        {
            return $"{SheetAssertion.Describe(
                $"column '{header}'", $"have {difference.ExpectedCount} values", _negated)} " +
                $"but it has {difference.ActualCount}.";
        }

        return $"{SheetAssertion.Describe(
            $"column '{header}' row {_table.DataStartRow + difference.Index}",
            $"be {Format(difference.Expected)}", _negated)} but it was {Format(difference.Actual)}.";
    }

    private static string Count(int count) => $"{count} {(count == 1 ? "value" : "values")}";

    private static string Format(string? value) => value is null ? "empty" : $"'{value}'";
}
