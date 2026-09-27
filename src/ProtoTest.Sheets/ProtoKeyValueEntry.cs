namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>
/// One label/value entry of a key-value sheet: the label the sheet carries, the value converted to the
/// model's property type, and the assertions. <see cref="Should"/> compares with the cell assertion's
/// own rules, text ordinals, numbers within 1e-6 and dates within a second.
/// </summary>
public sealed class ProtoKeyValueEntry<TValue>
{
    private readonly ProtoExecutionContext? _context;

    internal ProtoKeyValueEntry(
        string sheetName,
        string label,
        ProtoCell cell,
        TValue? value,
        ProtoExecutionContext? context)
    {
        SheetName = sheetName;
        Label = label;
        Cell = cell;
        Value = value;
        _context = context;
    }

    /// <summary>The label the sheet carries, as declared by the property's <c>[Column("...")]</c>.</summary>
    public string Label { get; }

    /// <summary>The cell's value converted to the model's property type; null for an empty cell.</summary>
    public TValue? Value { get; }

    /// <summary>The positive assertions of this entry, for example <c>Should.Be(123.45m)</c>.</summary>
    public ProtoKeyValueEntryAssertions<TValue> Should => new(this, negated: false);

    /// <summary>The negated assertions of this entry, for example <c>ShouldNot.Be(0m)</c>.</summary>
    public ProtoKeyValueEntryAssertions<TValue> ShouldNot => new(this, negated: true);

    internal ProtoCell Cell { get; }

    internal string SheetName { get; }

    internal ProtoExecutionContext? Context => _context;
}
