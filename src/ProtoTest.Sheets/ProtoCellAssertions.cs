namespace ProtoTest.Sheets;

using System.Globalization;
using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// The <c>Should</c>/<c>ShouldNot</c> surface of a cell. Each check is implemented once and its
/// polarity is fixed by the property that produced this facade, so a negative failure reads
/// "Expected ... not to ..." and carries the same evidence as the positive one.
/// </summary>
public sealed class ProtoCellAssertions
{
    private readonly ProtoCell _cell;
    private readonly string _sheetName;
    private readonly ProtoExecutionContext? _context;
    private readonly bool _negated;

    internal ProtoCellAssertions(
        ProtoCell cell,
        string sheetName,
        ProtoExecutionContext? context,
        bool negated)
    {
        _cell = cell;
        _sheetName = sheetName;
        _context = context;
        _negated = negated;
    }

    /// <summary>
    /// Asserts the cell holds the expected text, number, boolean or date typed from the file. A null
    /// expectation asserts the cell holds no text.
    /// </summary>
    public void Be(object? expected)
    {
        var (holds, expectation) = Compare(expected);
        Assert(holds, expectation);
    }

    /// <summary>Asserts the cell holds text.</summary>
    public void BeText() => Assert(_cell.Text is not null, "hold text");

    /// <summary>Asserts the cell is empty.</summary>
    public void BeBlank() => Assert(_cell.IsEmpty, "be blank");

    /// <summary>Asserts the cell holds exactly this formula (the cached value stays in the typed values).</summary>
    public void HaveFormula(string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        Assert(string.Equals(_cell.Formula, formula, StringComparison.Ordinal), $"hold the formula '{formula}'");
    }

    private (bool Holds, string Expectation) Compare(object? expected)
        => expected switch
        {
            null => (_cell.Text is null, "be empty"),
            string text => (string.Equals(_cell.Text, text, StringComparison.Ordinal), $"be '{text}'"),
            bool boolean => (_cell.Boolean == boolean, $"be {boolean}"),
            DateTime date => (
                _cell.Date is { } actual && Math.Abs((actual - date).TotalSeconds) < 1,
                $"be {date.ToString("O", CultureInfo.InvariantCulture)}"),
            double number => (NumberIs(number), $"be {Format(number)}"),
            decimal number => (NumberIs((double)number), $"be {number.ToString(CultureInfo.InvariantCulture)}"),
            float number => (NumberIs(number), $"be {Format(number)}"),
            int number => (NumberIs(number), $"be {Format(number)}"),
            long number => (NumberIs(number), $"be {Format(number)}"),
            short number => (NumberIs(number), $"be {Format(number)}"),
            byte number => (NumberIs(number), $"be {Format(number)}"),
            _ => throw new ArgumentException(
                $"A cell cannot be compared to a value of type {expected.GetType().Name}.", nameof(expected))
        };

    private bool NumberIs(double expected)
        => _cell.Number is { } actual && Math.Abs(actual - expected) <= 0.000001;

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private void Assert(bool holds, string expectation)
    {
        var reference = $"{_sheetName}!{_cell.Reference}";
        SheetAssertion.Run(
            _context,
            $"Sheets · {reference}",
            new Dictionary<string, string?>
            {
                ["sheets.cell"] = reference,
                ["sheets.expected"] = expectation,
                ["sheets.actual"] = _cell.Display()
            },
            _negated,
            () => holds,
            () => new SheetAssertionFailure(
                $"{SheetAssertion.Describe(reference, expectation, _negated)} but it was {_cell.Display()}."));
    }
}
