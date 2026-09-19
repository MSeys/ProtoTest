namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>
/// A header-aware view of a sheet area. Header rows can be layered - a merged group header over
/// subheaders - and a column is addressed by its full header path; a single segment matches when it is
/// unambiguous, and ambiguity fails with the candidate paths instead of guessing.
/// </summary>
public sealed class ProtoTable
{
    private readonly ProtoSheet _sheet;
    private readonly ProtoExecutionContext? _context;
    private readonly IReadOnlyList<IReadOnlyList<string>> _columns;

    internal ProtoTable(
        ProtoSheet sheet,
        int[] headerRows,
        IReadOnlyList<IReadOnlyList<string>> columns,
        int dataStartRow,
        ProtoExecutionContext? context)
    {
        _sheet = sheet;
        HeaderRows = headerRows;
        _columns = columns;
        DataStartRow = dataStartRow;
        _context = context;
    }

    /// <summary>The rows the headers were read from.</summary>
    public IReadOnlyList<int> HeaderRows { get; }

    /// <summary>One header path per column, top level first.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Headers => _columns;

    /// <summary>The first row that holds data.</summary>
    public int DataStartRow { get; }

    public int RowCount => Math.Max(0, _sheet.RowCount - DataStartRow + 1);

    public int ColumnCount => _columns.Count;

    public IReadOnlyList<ProtoTableRow> Rows
    {
        get
        {
            RecordRead(DataRange);
            return [.. Enumerable.Range(DataStartRow, RowCount).Select(row => new ProtoTableRow(this, row))];
        }
    }

    /// <summary>Finds the column whose header path matches; a single segment matches by suffix.</summary>
    public ProtoColumn Column(params string[] headerPath)
    {
        ArgumentNullException.ThrowIfNull(headerPath);
        var columnNumber = ColumnNumber(headerPath);
        RecordRead(ColumnRange(columnNumber));
        return new ProtoColumn(this, columnNumber);
    }

    /// <summary>Finds the row where one column holds a value, for example the EMEA row.</summary>
    public ProtoTableRow RowWhere(string keyColumn, string value)
        => RowWhere([keyColumn], value);

    /// <summary>Finds the row where a header path holds a value.</summary>
    public ProtoTableRow RowWhere(IReadOnlyList<string> keyHeaderPath, string value)
    {
        ArgumentNullException.ThrowIfNull(keyHeaderPath);
        ArgumentNullException.ThrowIfNull(value);
        var column = ColumnNumber([.. keyHeaderPath]);
        RecordRead(DataRange);
        return FindRow(column, value) is { } row
            ? new ProtoTableRow(this, row)
            : throw new SpreadsheetAssertionException(
                $"The table on '{_sheet.Name}' has no row where " +
                $"'{string.Join(" / ", keyHeaderPath)}' is '{value}'.");
    }

    /// <summary>The positive assertions of this table, for example <c>Should.ContainRow("Region", "EMEA")</c>.</summary>
    public ProtoTableAssertions Should => new(this, _sheet.Name, _context, negated: false);

    /// <summary>The negated assertions of this table, for example <c>ShouldNot.ContainRow("Region", "NOPE")</c>.</summary>
    public ProtoTableAssertions ShouldNot => new(this, _sheet.Name, _context, negated: true);

    internal ProtoExecutionContext? Context => _context;

    /// <summary>Whether any data row holds <paramref name="value"/> under the key column, recording the read.</summary>
    internal bool ContainsRow(string keyColumn, string value)
    {
        ArgumentNullException.ThrowIfNull(keyColumn);
        ArgumentNullException.ThrowIfNull(value);
        var column = ColumnNumber([keyColumn]);
        RecordRead(DataRange);
        return FindRow(column, value) is not null;
    }

    private int? FindRow(int column, string value)
    {
        foreach (var row in Enumerable.Range(DataStartRow, RowCount))
        {
            // Compare the rendered value, not the text: a typed key cell (a number, boolean or date)
            // has no Text and would never match its own printed form.
            if (string.Equals(_sheet.CellByNumber(row, column).RenderedValue, value, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
    }

    internal int ColumnNumber(IReadOnlyList<string> headerPath)
    {
        ArgumentNullException.ThrowIfNull(headerPath);
        if (headerPath.Count == 0 || headerPath.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A header path needs at least one non-empty segment.", nameof(headerPath));
        }

        var exact = Enumerable.Range(0, _columns.Count)
            .Where(index => _columns[index].Count == headerPath.Count
                && _columns[index].Zip(headerPath, (header, wanted) =>
                    string.Equals(header, wanted, StringComparison.Ordinal)).All(match => match))
            .ToArray();
        if (exact.Length == 1)
        {
            return exact[0] + 1;
        }

        if (exact.Length > 1)
        {
            throw new SpreadsheetAssertionException(
                $"The header path '{string.Join(" / ", headerPath)}' matches more than one column on '{_sheet.Name}'.");
        }

        var suffix = Enumerable.Range(0, _columns.Count)
            .Where(index => _columns[index].Count >= headerPath.Count
                && _columns[index].Skip(_columns[index].Count - headerPath.Count)
                    .Zip(headerPath, (header, wanted) => string.Equals(header, wanted, StringComparison.Ordinal))
                    .All(match => match))
            .ToArray();
        return suffix.Length switch
        {
            1 => suffix[0] + 1,
            0 => throw new SpreadsheetAssertionException(
                $"The table on '{_sheet.Name}' has no column '{string.Join(" / ", headerPath)}'. It has: " +
                string.Join(", ", _columns.Select(column => string.Join(" / ", column))) + "."),
            _ => throw new SpreadsheetAssertionException(
                $"The column '{string.Join(" / ", headerPath)}' is ambiguous on '{_sheet.Name}' " +
                "(it matches several groups); use the full header path.")
        };
    }

    internal ProtoCell Cell(int row, int column)
        => _sheet.CellByNumber(row, column);

    /// <summary>The data range the table covers, used to record a read of the whole table.</summary>
    internal string DataRange
        => _columns.Count == 0 || RowCount == 0
            ? _sheet.Name
            : $"{_sheet.Name}!A{DataStartRow}:{Internal.SheetReferences.Format(_columns.Count, DataStartRow + RowCount - 1)}";

    /// <summary>The data range of one column, used to record a read of that column.</summary>
    internal string ColumnRange(int columnNumber)
        => _columns.Count == 0 || RowCount == 0
            ? _sheet.Name
            : $"{_sheet.Name}!{Internal.SheetReferences.Format(columnNumber, DataStartRow)}:" +
              Internal.SheetReferences.Format(columnNumber, DataStartRow + RowCount - 1);

    /// <summary>The range of one data row, used to record a read of that row.</summary>
    internal string RowRange(int rowNumber)
        => _columns.Count == 0 || RowCount == 0
            ? _sheet.Name
            : $"{_sheet.Name}!A{rowNumber}:{Internal.SheetReferences.Format(_columns.Count, rowNumber)}";

    /// <summary>Records that the test read <paramref name="reference"/>; only actual reads reach here.</summary>
    internal void RecordRead(string reference)
        => _context?.RecordObservation(new ProtoObservation("Sheets", "sheets.range", reference));
}
