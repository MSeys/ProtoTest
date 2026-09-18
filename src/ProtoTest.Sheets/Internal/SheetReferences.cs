namespace ProtoTest.Sheets.Internal;

/// <summary>Parses and formats A1-style spreadsheet references.</summary>
internal static class SheetReferences
{
    public static (int Column, int Row) Parse(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var column = 0;
        var index = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        var row = 0;
        while (index < reference.Length && char.IsDigit(reference[index]))
        {
            row = (row * 10) + (reference[index] - '0');
            index++;
        }

        if (column == 0 || row == 0 || index != reference.Length)
        {
            throw new FormatException($"'{reference}' is not an A1-style cell reference.");
        }

        return (column, row);
    }

    public static string Format(int column, int row)
    {
        var letters = string.Empty;
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            column = (column - 1) / 26;
        }

        return $"{letters}{row}";
    }
}
