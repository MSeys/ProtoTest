namespace ProtoTest.Sheets;

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

    /// <summary>Reads the cell under the header path.</summary>
    public ProtoCell this[params string[] headerPath]
    {
        get
        {
            var cell = _table.Cell(RowNumber, _table.ColumnNumber(headerPath));
            _table.RecordRead(RowNumber);
            return cell;
        }
    }
}
