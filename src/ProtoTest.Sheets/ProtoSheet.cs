namespace ProtoTest.Sheets;

using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// One sheet. Reading a cell or range records a <c>sheets.range</c> observation, which is what the
/// coverage collector aggregates: coverage is the ranges the test actually read, not what the file has.
/// </summary>
public sealed class ProtoSheet
{
    private readonly Dictionary<string, ProtoCell> _cells;
    private readonly ProtoExecutionContext? _context;

    internal ProtoSheet(
        string name,
        int index,
        bool isHidden,
        IReadOnlyList<CellData> cells,
        int rowCount,
        int columnCount,
        ProtoExecutionContext? context)
    {
        Name = name;
        Index = index;
        IsHidden = isHidden;
        RowCount = rowCount;
        ColumnCount = columnCount;
        _context = context;
        _cells = cells.ToDictionary(
            cell => cell.Reference,
            cell => new ProtoCell(
                cell.Reference,
                name,
                cell.Text,
                cell.Number,
                cell.Boolean,
                cell.Date,
                cell.Formula,
                context),
            StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }

    /// <summary>The sheet's position in the workbook, hidden sheets included.</summary>
    public int Index { get; }

    public bool IsHidden { get; }

    /// <summary>Rows in the used range.</summary>
    public int RowCount { get; }

    /// <summary>Columns in the used range.</summary>
    public int ColumnCount { get; }

    /// <summary>Reads one cell; a reference outside the file is an empty cell, never an error.</summary>
    public ProtoCell Cell(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var normalized = reference.ToUpperInvariant();
        Record(normalized);
        return CellByNumber(
            SheetReferences.Parse(normalized).Row,
            SheetReferences.Parse(normalized).Column);
    }

    /// <summary>Reads a cell by coordinates; row and column are 1-based.</summary>
    public ProtoCell Cell(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        var reference = SheetReferences.Format(column, row);
        Record(reference);
        return CellByNumber(row, column);
    }

    /// <summary>
    /// Header-aware view of the sheet. Pass one or more header rows; a merged group header spanning
    /// columns plus the subheaders under it become one header path per column.
    /// </summary>
    public ProtoTable Table(params int[] headerRows)
    {
        int[] rows = headerRows.Length == 0 ? [1] : [.. headerRows.Order()];
        foreach (var row in rows)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(row, 1);
        }

        var columns = new List<IReadOnlyList<string>>();
        for (var column = 1; column <= ColumnCount; column++)
        {
            columns.Add([.. rows
                .Select(row => CellByNumber(row, column).Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)]);
        }

        var table = new ProtoTable(this, rows, columns, rows[^1] + 1, _context);
        table.RecordRead();
        return table;
    }

    internal ProtoCell CellByNumber(int row, int column)
    {
        var reference = SheetReferences.Format(column, row);
        return _cells.TryGetValue(reference, out var cell)
            ? cell
            : ProtoCell.Empty(reference, Name, _context);
    }

    /// <summary>Reads a range like <c>A1:C10</c>, row by row.</summary>
    public ProtoRange Range(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var normalized = reference.ToUpperInvariant();
        var parts = normalized.Split(':', 2);
        var (startColumn, startRow) = SheetReferences.Parse(parts[0]);
        var (endColumn, endRow) = parts.Length == 2 ? SheetReferences.Parse(parts[1]) : (startColumn, startRow);
        var rows = new List<IReadOnlyList<ProtoCell>>();
        for (var row = startRow; row <= endRow; row++)
        {
            var cells = new List<ProtoCell>();
            for (var column = startColumn; column <= endColumn; column++)
            {
                var cellReference = SheetReferences.Format(column, row);
                cells.Add(_cells.TryGetValue(cellReference, out var cell)
                    ? cell
                    : ProtoCell.Empty(cellReference, Name, _context));
            }

            rows.Add(cells);
        }

        Record(normalized);
        return new ProtoRange(normalized, rows, Name, _context);
    }

    internal string Display() => $"{Name} ({RowCount}x{ColumnCount})";

    private void Record(string reference)
        => _context?.RecordObservation(new ProtoObservation("Sheets", "sheets.range", $"{Name}!{reference}"));
}
