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
        => [.. Cells.Select(cell => cell.RenderedValue)];

    /// <summary>The positive assertions of this column, for example <c>Should.Be(["1200", "900"])</c>.</summary>
    public ProtoColumnAssertions Should => new(this, _table, negated: false);

    /// <summary>The negated assertions of this column, for example <c>ShouldNot.Be([...])</c>.</summary>
    public ProtoColumnAssertions ShouldNot => new(this, _table, negated: true);
}
