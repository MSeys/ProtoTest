namespace ProtoTest.Sheets;

using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> surface of a table. The row lookup runs once and the polarity of
/// the property that produced this facade decides whether finding the row passes or fails.
/// </summary>
public sealed class ProtoTableAssertions
{
    private readonly ProtoTable _table;
    private readonly string _sheetName;
    private readonly ProtoExecutionContext? _context;
    private readonly bool _negated;

    internal ProtoTableAssertions(
        ProtoTable table,
        string sheetName,
        ProtoExecutionContext? context,
        bool negated)
    {
        _table = table;
        _sheetName = sheetName;
        _context = context;
        _negated = negated;
    }

    /// <summary>Asserts a row exists where the key column holds the value.</summary>
    public void ContainRow(string keyColumn, string value)
    {
        ArgumentNullException.ThrowIfNull(keyColumn);
        ArgumentNullException.ThrowIfNull(value);
        SheetAssertion.Run(
            _context,
            $"Sheets · {_sheetName}",
            new Dictionary<string, string?>
            {
                ["sheets.expected"] = $"a row where '{keyColumn}' is '{value}'"
            },
            _negated,
            () => _table.ContainsRow(keyColumn, value),
            () => new SheetAssertionFailure(
                $"{SheetAssertion.Describe(
                    $"the table on '{_sheetName}'",
                    $"contain a row where '{keyColumn}' is '{value}'", _negated)} " +
                $"but it {(_negated ? "does" : "does not")}."));
    }
}
