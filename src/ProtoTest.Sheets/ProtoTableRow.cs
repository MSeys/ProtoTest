namespace ProtoTest.Sheets;

using System.Text.Json;

/// <summary>One data row of a table, addressable by header path (or a single unambiguous segment).</summary>
public sealed class ProtoTableRow
{
    private readonly ProtoTable _table;

    internal ProtoTableRow(ProtoTable table, int rowNumber)
    {
        _table = table;
        RowNumber = rowNumber;
    }

    public int RowNumber { get; }

    /// <summary>The assertions of this row, for example <c>Should.MatchShape(shape)</c>.</summary>
    public ProtoTableRowAssertions Should => new(this);

    /// <summary>Reads the cell under the header path.</summary>
    public ProtoCell this[params string[] headerPath]
    {
        get
        {
            var cell = _table.Cell(RowNumber, _table.ColumnNumber(headerPath));
            _table.RecordRead(_table.RowRange(RowNumber));
            return cell;
        }
    }

    /// <summary>
    /// Projects the row onto a shape object keyed by each column's leaf header name, with the cell's
    /// rendered value (an empty cell is null). Two columns whose leaf names differ only by case cannot
    /// be told apart either, because the shape lookup is case-insensitive by default, so they fail here
    /// instead of silently overwriting one another.
    /// </summary>
    internal IReadOnlyDictionary<string, object?> ShapeValues()
    {
        _table.RecordRead(_table.RowRange(RowNumber));
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= _table.ColumnCount; column++)
        {
            var headerPath = _table.Headers[column - 1];
            var leaf = headerPath[^1];
            if (values.ContainsKey(leaf))
            {
                throw new SpreadsheetAssertionException(
                    $"The table on '{_table.SheetName}' has more than one column whose header ends in " +
                    $"'{leaf}', so a row shape cannot tell them apart; assert with Table.Column on the " +
                    "full header path instead.");
            }

            values[leaf] = _table.Cell(RowNumber, column).RenderedValue;
        }

        return values;
    }

    /// <summary>
    /// The shape assertion behind <see cref="ProtoTableRowAssertions.MatchShape"/> and the obsolete
    /// extension shim. The subject is the row's <c>Sheet!Range</c> reference.
    /// </summary>
    internal ProtoTableRow AssertShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        var actual = JsonSerializer.SerializeToElement(ShapeValues(), options);
        SheetModelAssertions.AssertShape(
            actual,
            nameof(ProtoTableRow),
            _table.RowRange(RowNumber),
            expectedShape,
            options);
        return this;
    }
}
