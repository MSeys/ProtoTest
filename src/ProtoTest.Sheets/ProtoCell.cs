namespace ProtoTest.Sheets;

using System.Globalization;
using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

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

    public void ShouldBe(string? expected)
        => Assert(
            string.Equals(Text, expected, StringComparison.Ordinal),
            expected is null ? "be empty" : $"be '{expected}'");

    public void ShouldBe(double expected, double tolerance = 0.000001)
        => Assert(
            Number is { } actual && Math.Abs(actual - expected) <= tolerance,
            $"be {expected.ToString(CultureInfo.InvariantCulture)}");

    public void ShouldBe(bool expected)
        => Assert(Boolean == expected, $"be {expected}");

    public void ShouldBe(DateTime expected)
        => Assert(
            Date is { } actual && Math.Abs((actual - expected).TotalSeconds) < 1,
            $"be {expected.ToString("O", CultureInfo.InvariantCulture)}");

    /// <summary>Asserts the cell holds text.</summary>
    public void ShouldBeText()
        => Assert(Text is not null, "hold text");

    /// <summary>Asserts the cell is empty.</summary>
    public void ShouldBeBlank()
        => Assert(IsEmpty, "be blank");

    /// <summary>Asserts the cell holds exactly this formula (the cached value stays in the typed values).</summary>
    public void ShouldHaveFormula(string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        Assert(string.Equals(Formula, formula, StringComparison.Ordinal), $"hold the formula '{formula}'");
    }

    internal static ProtoCell Empty(string reference, string sheetName, ProtoExecutionContext? context)
        => new(reference, sheetName, null, null, null, null, null, context);

    internal string Display()
        => IsEmpty
            ? "<empty>"
            : Text
              ?? Number?.ToString(CultureInfo.InvariantCulture)
              ?? Boolean?.ToString()
              ?? Date?.ToString("O", CultureInfo.InvariantCulture)
              ?? "<empty>";

    private void Assert(bool passed, string phrase)
    {
        using var operation = _context?.Trace
            .Operation("sheets.assert", $"Sheets · {_sheetName}!{Reference}", "ProtoTest.Sheets")
            .With("sheets.cell", $"{_sheetName}!{Reference}")
            .With("sheets.expected", phrase)
            .With("sheets.actual", Display())
            .Begin();
        if (passed)
        {
            operation?.Succeed();
            return;
        }

        var exception = new SpreadsheetAssertionException(
            $"Expected {_sheetName}!{Reference} to {phrase} but it was {Display()}.");
        operation?.Fail(exception);
        throw exception;
    }
}
