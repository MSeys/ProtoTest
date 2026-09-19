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

    /// <summary>The positive assertions of this range, for example <c>Should.Match([...])</c>.</summary>
    public ProtoRangeAssertions Should => new(this, _sheetName, _context, negated: false);

    /// <summary>The negated assertions of this range, for example <c>ShouldNot.HaveDimensions(3, 3)</c>.</summary>
    public ProtoRangeAssertions ShouldNot => new(this, _sheetName, _context, negated: true);
}
