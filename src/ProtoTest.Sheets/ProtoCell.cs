namespace ProtoTest.Sheets;

using System.Globalization;
using ProtoTest.Core;

/// <summary>
/// One cell. Values are typed best-effort from the OpenXML cell type and number format: text, number,
/// boolean or date, with the formula text kept alongside its cached value. Assertions are traced and
/// fail with the sheet, reference, expected and actual value.
/// </summary>
public sealed class ProtoCell
{
    private readonly ProtoExecutionContext? _context;
    private readonly string _sheetName;

    internal ProtoCell(
        string reference,
        string sheetName,
        string? text,
        double? number,
        bool? boolean,
        DateTime? date,
        string? formula,
        ProtoExecutionContext? context)
    {
        Reference = reference;
        _sheetName = sheetName;
        Text = text;
        Number = number;
        Boolean = boolean;
        Date = date;
        Formula = formula;
        _context = context;
    }

    public string Reference { get; }

    public string? Text { get; }

    public double? Number { get; }

    public bool? Boolean { get; }

    public DateTime? Date { get; }

    /// <summary>The formula text when the cell holds one; the cached result is in the typed values.</summary>
    public string? Formula { get; }

    public bool IsEmpty => Text is null && Number is null && Boolean is null && Date is null;

    /// <summary>The positive assertions of this cell, for example <c>Should.Be("Total")</c>.</summary>
    public ProtoCellAssertions Should => new(this, _sheetName, _context, negated: false);

    /// <summary>The negated assertions of this cell, for example <c>ShouldNot.BeBlank()</c>.</summary>
    public ProtoCellAssertions ShouldNot => new(this, _sheetName, _context, negated: true);

    internal static ProtoCell Empty(string reference, string sheetName, ProtoExecutionContext? context)
        => new(reference, sheetName, null, null, null, null, null, context);

    /// <summary>The typed rendering used by column and range comparisons: text, else the typed value.</summary>
    internal string? RenderedValue
        => Text
            ?? Number?.ToString(CultureInfo.InvariantCulture)
            ?? Boolean?.ToString()
            ?? Date?.ToString("O", CultureInfo.InvariantCulture);

    internal string Display()
        => IsEmpty
            ? "<empty>"
            : Text
              ?? Number?.ToString(CultureInfo.InvariantCulture)
              ?? Boolean?.ToString()
              ?? Date?.ToString("O", CultureInfo.InvariantCulture)
              ?? "<empty>";
}
