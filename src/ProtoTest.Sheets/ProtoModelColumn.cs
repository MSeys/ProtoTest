namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>A typed model column with property-style assertions over its values.</summary>
public sealed class ProtoModelColumn<TValue>
{
    private readonly ProtoExecutionContext? _context;
    private readonly string _sheetName;
    private readonly int _dataStartRow;

    internal ProtoModelColumn(
        string sheetName,
        string header,
        IReadOnlyList<TValue?> values,
        int dataStartRow,
        ProtoExecutionContext? context)
    {
        _sheetName = sheetName;
        Header = header;
        Values = values;
        _dataStartRow = dataStartRow;
        _context = context;
    }

    public string Header { get; }

    public IReadOnlyList<TValue?> Values { get; }

    /// <summary>The positive assertions of this column, for example <c>Should.Be([1200m, 900m])</c>.</summary>
    public ProtoModelColumnAssertions<TValue> Should
        => new(this, _sheetName, _dataStartRow, _context, negated: false);

    /// <summary>The negated assertions of this column, for example <c>ShouldNot.BeSortedBy()</c>.</summary>
    public ProtoModelColumnAssertions<TValue> ShouldNot
        => new(this, _sheetName, _dataStartRow, _context, negated: true);

    /// <summary>Checks every value against a property, for example every amount above zero.</summary>
    /// <remarks>Obsolete: use <c>Should.All(predicate)</c>.</remarks>
    [Obsolete("Use Should.All(predicate) instead.")]
    public void ShouldAll(Func<TValue?, bool> predicate) => Should.All(predicate);

    internal string Title => $"Sheets · {_sheetName}.{Header}";

    internal IReadOnlyDictionary<string, string?> ColumnAttributes
        => new Dictionary<string, string?> { ["sheets.column"] = $"{_sheetName}.{Header}" };

    internal static string Display(TValue? value)
        => value?.ToString() ?? "<empty>";
}
