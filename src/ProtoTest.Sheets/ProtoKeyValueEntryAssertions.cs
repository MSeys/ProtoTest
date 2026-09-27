namespace ProtoTest.Sheets;

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> surface of one key-value entry. The comparison is the cell
/// assertion's own, so an entry asserts exactly like the cell it reads, and the failure names the
/// label beside the cell.
/// </summary>
public sealed class ProtoKeyValueEntryAssertions<TValue>
{
    private readonly ProtoKeyValueEntry<TValue> _entry;
    private readonly bool _negated;

    internal ProtoKeyValueEntryAssertions(ProtoKeyValueEntry<TValue> entry, bool negated)
    {
        _entry = entry;
        _negated = negated;
    }

    /// <summary>
    /// Asserts the entry's cell holds the expected text, number, boolean or date typed from the file;
    /// a null expectation asserts the cell holds no text. Returns the entry.
    /// </summary>
    public ProtoKeyValueEntry<TValue> Be(TValue? expected)
    {
        new ProtoCellAssertions(_entry.Cell, _entry.SheetName, _entry.Context, _negated, _entry.Label)
            .Be((object?)expected);
        return _entry;
    }
}
